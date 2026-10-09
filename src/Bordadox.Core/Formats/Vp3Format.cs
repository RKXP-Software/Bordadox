using System.Text;
using Bordadox.Core.Stitches;

namespace Bordadox.Core.Formats;

/// <summary>
/// Formato Pfaff/Husqvarna Viking VP3: tudo em big-endian, com blocos que guardam o próprio tamanho (arquivo, desenho, bloco de cor,
/// bloco de pontos). Cada bloco de cor tem a linha (RGB e nome), a posição inicial e os pontos em registros (dx, dy) de 0,1 mm,
/// ou, quando passam de ±127, na forma longa (0x80 0x01, dx e dy de 16 bits, 0x80 0x02). 0x80 0x03 é o corte. As posições e os limites
/// do cabeçalho são gravados em milésimos de milímetro.
/// O VP3 não tem "salto": ao gravar, cada salto vira corte + movimento embutido no ponto seguinte. Escrito a partir da especificação do
/// formato; ainda não validado num software ou máquina Pfaff/Viking.
/// </summary>
public sealed class Vp3Format : IEmbroideryFormat
{
    public string Description => "Pfaff/Viking VP3";
    public string Extension => "vp3";
    public bool CanRead => true;
    public bool CanWrite => true;

    private const string Producer = "Produced by     Software Ltd";

    // ---------- Leitura ----------

    public StitchPattern Read(Stream stream)
    {
        var pattern = new StitchPattern();
        var r = new BigEndianReader(stream);

        r.Skip(6);                       // "%vsm%" e um zero
        r.SkipString();                  // produtor
        r.Skip(7);                       // 00 02 00 e o tamanho do arquivo
        r.SkipString();                  // notas
        r.Skip(32);                      // limites, número de pontos, contagens e o início do bloco do desenho
        int centerX = r.Int32() / 100;
        int centerY = -(r.Int32() / 100);
        r.Skip(27);
        r.SkipString();
        r.Skip(24);
        r.SkipString();
        int colors = r.UInt16();
        if (colors < 1 || colors > 1000) throw new InvalidDataException("Arquivo VP3 inválido (número de cores).");

        int x = 0, y = 0;
        for (int i = 0; i < colors; i++)
        {
            r.Skip(3);                   // 00 05 00
            int size = r.Int32();
            long blockEnd = stream.Position + size;
            int startX = r.Int32() / 100 + centerX;
            int startY = -(r.Int32() / 100) + centerY;

            pattern.Threads.Add(ReadThread(r));
            r.Skip(15);                  // deslocamento do bloco (8) e o cabeçalho do bloco de pontos (7)
            r.Skip(3);                   // 0A F6 00
            int dataLength = (int)(blockEnd - stream.Position);
            if (dataLength < 0) throw new InvalidDataException("Arquivo VP3 inválido (tamanho de bloco).");
            var data = r.Bytes(dataLength);

            if (i > 0)
            {
                pattern.Add(x, y, StitchCommand.ColorChange);
                if (startX != x || startY != y) { x = startX; y = startY; pattern.Add(x, y, StitchCommand.Jump); }
            }
            else if (startX != 0 || startY != 0)
            {
                x = startX; y = startY;
                pattern.Add(x, y, StitchCommand.Jump);
            }
            DecodeStitches(data, pattern, ref x, ref y);
        }
        pattern.Add(x, y, StitchCommand.End);
        return pattern;
    }

    private static ThreadColor ReadThread(BigEndianReader r)
    {
        int count = r.Byte();
        r.Byte();                                    // transição
        int rgb = 0;
        for (int i = 0; i < count; i++)
        {
            rgb = r.UInt24();                        // vale a última
            r.Byte();
            r.UInt16();
        }
        r.Byte();                                    // tipo
        r.Byte();                                    // espessura
        string catalog = r.String(), description = r.String();
        r.String();                                  // marca
        return new ThreadColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, description.Length > 0 ? description : $"#{rgb:X6}", catalog);
    }

    /// <summary>Depois de um corte a agulha vai até o ponto sem costurar (salto) e só então fura o tecido (ponto).</summary>
    private static void AddStitch(StitchPattern pattern, int x, int y, bool afterTrim)
    {
        if (afterTrim) pattern.Add(x, y, StitchCommand.Jump);
        pattern.Add(x, y, StitchCommand.Stitch);
    }

    private static void DecodeStitches(byte[] data, StitchPattern pattern, ref int x, ref int y)
    {
        bool afterTrim = false;
        for (int i = 0; i + 1 < data.Length;)
        {
            if (data[i] != 0x80)
            {
                x += (sbyte)data[i];
                y += (sbyte)data[i + 1];
                AddStitch(pattern, x, y, afterTrim);
                afterTrim = false;
                i += 2;
                continue;
            }
            byte code = data[i + 1];
            i += 2;
            if (code == 0x01 && i + 3 < data.Length)
            {
                x += (short)((data[i] << 8) | data[i + 1]);
                y += (short)((data[i + 2] << 8) | data[i + 3]);
                AddStitch(pattern, x, y, afterTrim);
                afterTrim = false;
                i += 6;                              // dx, dy e o par 0x80 0x02 que fecha o registro
            }
            else if (code == 0x03)
            {
                pattern.Add(x, y, StitchCommand.Trim);
                afterTrim = true;
            }
        }
    }

    // ---------- Gravação ----------

    public void Write(StitchPattern source, Stream stream)
    {
        var pattern = source.Centered();
        pattern.FixThreadCount();
        var b = pattern.Bounds();
        double width = b.MaxX - b.MinX, height = b.MaxY - b.MinY;
        double halfW = width / 2, halfH = height / 2;
        double centerX = b.MaxX - halfW, centerY = b.MaxY - halfH;

        var all = pattern.Stitches.ToList();
        var blocks = new List<List<Stitch>>();
        foreach (var s in all)
        {
            if (blocks.Count == 0 || s.Command == StitchCommand.ColorChange) blocks.Add(new List<Stitch>());
            blocks[^1].Add(s);
        }
        if (blocks.Count == 0) blocks.Add(new List<Stitch>());
        int stitchCount = all.Count(s => s.Command != StitchCommand.End);

        var w = new BigEndianWriter(stream);
        w.Ascii("%vsm%");
        w.Byte(0);
        w.String16(Producer);

        // Corpo do arquivo
        w.Bytes(0x00, 0x02, 0x00);
        int fileSize = w.Placeholder();
        w.UInt16(0);                                   // notas
        w.Int32((int)(b.MaxX * 100.0));
        w.Int32((int)(b.MinY * -100.0));
        w.Int32((int)(b.MinX * 100.0));
        w.Int32((int)(b.MaxY * -100.0));
        w.Int32(stitchCount);
        w.Byte(0);
        w.Byte(blocks.Count);
        w.Byte(12);
        w.Byte(0);
        w.Byte(1);                                     // um desenho

        // Desenho
        w.Bytes(0x00, 0x03, 0x00);
        int designSize = w.Placeholder();
        w.Int32((int)centerX * 100);
        w.Int32((int)centerY * -100);
        w.Bytes(0, 0, 0);
        w.Int32((int)halfW * -100);
        w.Int32((int)halfW * 100);
        w.Int32((int)halfH * -100);
        w.Int32((int)halfH * 100);
        w.Int32((int)width * 100);
        w.Int32((int)height * 100);
        w.UInt16(0);                                   // notas
        w.Bytes(0x64, 0x64);
        w.Int32(4096); w.Int32(0); w.Int32(0); w.Int32(4096);
        w.Bytes(0x78, 0x78, 0x50, 0x50, 0x01, 0x00);
        w.String16(Producer);
        w.UInt16(blocks.Count);

        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var thread = pattern.Threads[Math.Min(i, pattern.Threads.Count - 1)];
            int firstX = 0, firstY = 0, lastX = 0, lastY = 0;
            if (block.Count > 0)
            {
                if (i > 0) { firstX = block[0].X; firstY = block[0].Y; }
                lastX = block[^1].X; lastY = block[^1].Y;
            }

            w.Bytes(0x00, 0x05, 0x00);
            int blockSize = w.Placeholder();
            // Relativo ao centro já truncado (o mesmo valor que se grava no desenho), para a leitura voltar exatamente à posição.
            w.Int32((firstX - (int)centerX) * 100);
            w.Int32(-(firstY - (int)centerY) * 100);

            w.Bytes(0x01, 0x00);
            w.UInt24((thread.R << 16) | (thread.G << 8) | thread.B);
            w.Bytes(0x00, 0x00, 0x00, 0x05, 0x28);
            w.String8(thread.Code ?? "");
            w.String8(thread.Name);
            w.String8("Brother");

            w.Int32((lastX - firstX) * 100);
            w.Int32(-(lastY - firstY) * 100);

            w.Bytes(0x00, 0x01, 0x00);
            int dataSize = w.Placeholder();
            w.Bytes(0x0A, 0xF6, 0x00);
            WriteStitches(w, block, firstX, firstY);
            w.Patch(dataSize);
            w.Byte(0);
            w.Patch(blockSize);
        }
        w.Patch(designSize);
        w.Patch(fileSize);
    }

    private static void WriteStitches(BigEndianWriter w, List<Stitch> block, int firstX, int firstY)
    {
        int lastX = firstX, lastY = firstY;
        bool trimmed = false;
        foreach (var s in block)
        {
            switch (s.Command)
            {
                case StitchCommand.Stitch:
                    // O deslocamento é sempre em relação ao último ponto GRAVADO: o movimento de um salto entra no primeiro ponto depois do corte.
                    int dx = s.X - lastX, dy = s.Y - lastY;
                    lastX = s.X; lastY = s.Y;
                    if (dx is >= -127 and <= 127 && dy is >= -127 and <= 127) { w.SByte(dx); w.SByte(dy); }
                    else { w.Bytes(0x80, 0x01); w.Int16(dx); w.Int16(dy); w.Bytes(0x80, 0x02); }
                    trimmed = false;
                    break;
                case StitchCommand.Jump:
                    // Sem salto no formato: corte, e o movimento vai embutido no próximo ponto.
                    if (!trimmed) { w.Bytes(0x80, 0x03); trimmed = true; }
                    break;
                case StitchCommand.Trim:
                    if (!trimmed) { w.Bytes(0x80, 0x03); trimmed = true; }
                    break;
                case StitchCommand.End:
                    w.Bytes(0x80, 0x03);
                    return;
            }
        }
    }

    // ---------- Leitura e gravação de bytes em big-endian ----------

    private sealed class BigEndianReader(Stream s)
    {
        public void Skip(int n) => Bytes(n);

        public byte[] Bytes(int n)
        {
            var buf = new byte[n];
            int read = 0;
            while (read < n)
            {
                int k = s.Read(buf, read, n - read);
                if (k <= 0) throw new EndOfStreamException("Arquivo VP3 truncado.");
                read += k;
            }
            return buf;
        }

        public int Byte() => Bytes(1)[0];
        public int UInt16() { var b = Bytes(2); return (b[0] << 8) | b[1]; }
        public int UInt24() { var b = Bytes(3); return (b[0] << 16) | (b[1] << 8) | b[2]; }
        public int Int32() { var b = Bytes(4); return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]; }
        public void SkipString() => Skip(UInt16());
        public string String() => Encoding.UTF8.GetString(Bytes(UInt16()));
    }

    private sealed class BigEndianWriter(Stream s)
    {
        public void Byte(int v) => s.WriteByte((byte)v);
        public void SByte(int v) => s.WriteByte((byte)(sbyte)v);
        public void Raw(byte[] v) => s.Write(v, 0, v.Length);
        public void Bytes(params int[] v) { foreach (var b in v) s.WriteByte((byte)b); }
        public void Ascii(string v) => Raw(Encoding.ASCII.GetBytes(v));
        public void UInt16(int v) { Byte(v >> 8); Byte(v); }
        public void Int16(int v) => UInt16(v & 0xFFFF);
        public void UInt24(int v) { Byte(v >> 16); Byte(v >> 8); Byte(v); }
        public void Int32(int v) { Byte(v >> 24); Byte(v >> 16); Byte(v >> 8); Byte(v); }

        /// <summary>Texto em UTF-16 big-endian com o tamanho em bytes na frente.</summary>
        public void String16(string v)
        {
            var bytes = Encoding.BigEndianUnicode.GetBytes(v);
            UInt16(bytes.Length);
            Raw(bytes);
        }

        /// <summary>Texto em UTF-8 com o número de caracteres na frente.</summary>
        public void String8(string v)
        {
            UInt16(v.Length);
            Raw(Encoding.UTF8.GetBytes(v));
        }

        /// <summary>Reserva 4 bytes para um tamanho e devolve a posição, para <see cref="Patch"/> preencher depois.</summary>
        public int Placeholder()
        {
            int pos = (int)s.Position;
            Int32(0);
            return pos;
        }

        /// <summary>Grava no marcador o número de bytes entre ele e a posição atual.</summary>
        public void Patch(int placeholder)
        {
            long end = s.Position;
            s.Position = placeholder;
            Int32((int)(end - placeholder - 4));
            s.Position = end;
        }
    }
}
