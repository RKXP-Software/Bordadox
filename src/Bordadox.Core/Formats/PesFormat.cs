using System.Text;
using Bordadox.Core.Stitches;

namespace Bordadox.Core.Formats;

/// <summary>
/// Formato PES (Brother/Babylock). As máquinas Brother leem a seção PEC embutida no arquivo,
/// então gravamos um PES versão 1 mínimo seguido da seção PEC completa (pontos + miniaturas).
/// </summary>
public sealed class PesFormat : IEmbroideryFormat
{
    public string Description => "Brother PES";
    public string Extension => "pes";
    public bool CanRead => true;
    public bool CanWrite => true;

    private const int IconWidth = 48;
    private const int IconHeight = 38;
    private const int IconStride = IconWidth / 8;

    public StitchPattern Read(Stream stream)
    {
        using var br = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var sig = Encoding.ASCII.GetString(br.ReadBytes(4));
        if (sig != "#PES") throw new InvalidDataException("Arquivo não é PES.");
        stream.Seek(8, SeekOrigin.Begin);
        int pecStart = br.ReadInt32();
        return ReadPec(stream, pecStart);
    }

    /// <summary>Lê a seção PEC a partir de <paramref name="pecStart"/>.</summary>
    public static StitchPattern ReadPec(Stream stream, long pecStart)
    {
        using var br = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var pattern = new StitchPattern();

        stream.Seek(pecStart + 3, SeekOrigin.Begin);
        pattern.Name = Encoding.ASCII.GetString(br.ReadBytes(16)).Trim();

        stream.Seek(pecStart + 48, SeekOrigin.Begin);
        int colorCount = br.ReadByte() + 1;
        var colorIdx = br.ReadBytes(colorCount);
        foreach (var idx in colorIdx)
            pattern.Threads.Add(BrotherPalette.Colors[idx < BrotherPalette.Colors.Count ? idx : 0]);

        stream.Seek(pecStart + 528, SeekOrigin.Begin);
        int x = 0, y = 0;
        while (stream.Position < stream.Length - 1)
        {
            int b1 = br.ReadByte();
            int b2 = br.ReadByte();
            if (b1 == 0xFF) break;
            if (b1 == 0xFE && b2 == 0xB0)
            {
                br.ReadByte();
                pattern.Add(x, y, StitchCommand.ColorChange);
                continue;
            }

            bool jump = false, trim = false;
            int dx, dy;
            if ((b1 & 0x80) != 0)
            {
                trim |= (b1 & 0x20) != 0;
                jump |= (b1 & 0x10) != 0;
                dx = Signed12(((b1 << 8) | b2) & 0x0FFF);
                b2 = br.ReadByte();
            }
            else dx = Signed7(b1);

            if ((b2 & 0x80) != 0)
            {
                trim |= (b2 & 0x20) != 0;
                jump |= (b2 & 0x10) != 0;
                int b3 = br.ReadByte();
                dy = Signed12(((b2 << 8) | b3) & 0x0FFF);
            }
            else dy = Signed7(b2);

            x += dx; y += dy;
            if (trim) pattern.Add(x - dx, y - dy, StitchCommand.Trim);
            pattern.Add(x, y, jump || trim ? StitchCommand.Jump : StitchCommand.Stitch);
        }
        pattern.Add(x, y, StitchCommand.End);
        return pattern;
    }

    private static int Signed7(int v) => v > 63 ? v - 128 : v;
    private static int Signed12(int v) => v > 2047 ? v - 4096 : v;

    public void Write(StitchPattern source, Stream stream)
    {
        var pattern = source.Centered().WithMaxMove(2000);
        pattern.FixThreadCount();

        using var bw = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        // Cabeçalho PES v1 "truncado": assinatura, offset do PEC e 10 bytes zerados.
        bw.Write(Encoding.ASCII.GetBytes("#PES0001"));
        bw.Write(22);
        bw.Write(new byte[10]);
        WritePec(pattern, bw);
    }

    private static void WritePec(StitchPattern pattern, BinaryWriter bw)
    {
        var stream = bw.BaseStream;
        long pecStart = stream.Position;

        // Índices de cor PEC: um por bloco de cor.
        var colorIdx = pattern.Threads.Select(t => (byte)BrotherPalette.NearestIndex(t)).ToArray();

        var name = new string((pattern.Name ?? "").Where(c => c < 128 && !char.IsControl(c)).Take(8).ToArray());
        bw.Write(Encoding.ASCII.GetBytes("LA:" + name.PadRight(16) + "\r"));
        bw.Write(Enumerable.Repeat((byte)0x20, 12).ToArray());
        bw.Write((byte)0xFF);
        bw.Write((byte)0x00);
        bw.Write((byte)IconStride);
        bw.Write((byte)IconHeight);
        bw.Write(Enumerable.Repeat((byte)0x20, 12).ToArray());
        bw.Write((byte)(colorIdx.Length - 1));
        bw.Write(colorIdx);
        bw.Write(Enumerable.Repeat((byte)0x20, 463 - colorIdx.Length).ToArray());

        // Bloco de pontos.
        long blockStart = stream.Position; // pecStart + 512
        var b = pattern.Bounds();
        bw.Write((short)0);
        bw.Write(new byte[3]); // placeholder: tamanho do bloco (int24)
        bw.Write(new byte[] { 0x31, 0xFF, 0xF0 });
        bw.Write((short)(b.MaxX - b.MinX));
        bw.Write((short)(b.MaxY - b.MinY));
        bw.Write((short)0x1E0);
        bw.Write((short)0x1B0);
        EncodeStitches(pattern, bw);
        long blockEnd = stream.Position;

        int blockLen = (int)(blockEnd - blockStart);
        stream.Seek(blockStart + 2, SeekOrigin.Begin);
        bw.Write((byte)(blockLen & 0xFF));
        bw.Write((byte)((blockLen >> 8) & 0xFF));
        bw.Write((byte)((blockLen >> 16) & 0xFF));
        stream.Seek(blockEnd, SeekOrigin.Begin);

        WriteIcons(pattern, bw);
        _ = pecStart;
    }

    private static void EncodeStitches(StitchPattern pattern, BinaryWriter bw)
    {
        int x = 0, y = 0;
        bool pendingTrim = false;
        bool colorTwo = true;

        void WriteLong(int dx, int dy, int flag)
        {
            int ex = (dx & 0x0FFF) | 0x8000 | flag;
            int ey = (dy & 0x0FFF) | 0x8000 | flag;
            bw.Write((byte)(ex >> 8)); bw.Write((byte)(ex & 0xFF));
            bw.Write((byte)(ey >> 8)); bw.Write((byte)(ey & 0xFF));
        }

        void FlushTrim()
        {
            if (!pendingTrim) return;
            WriteLong(0, 0, 0x2000);
            pendingTrim = false;
        }

        foreach (var s in pattern.Stitches)
        {
            int dx = s.X - x, dy = s.Y - y;
            switch (s.Command)
            {
                case StitchCommand.Stitch:
                    FlushTrim();
                    if (dx is >= -64 and <= 63 && dy is >= -64 and <= 63)
                    {
                        bw.Write((byte)(dx & 0x7F));
                        bw.Write((byte)(dy & 0x7F));
                    }
                    else WriteLong(dx, dy, 0);
                    x = s.X; y = s.Y;
                    break;
                case StitchCommand.Jump:
                    WriteLong(dx, dy, pendingTrim ? 0x2000 : 0x1000);
                    pendingTrim = false;
                    x = s.X; y = s.Y;
                    break;
                case StitchCommand.Trim:
                    pendingTrim = true;
                    break;
                case StitchCommand.ColorChange:
                case StitchCommand.Stop:
                    FlushTrim();
                    bw.Write((byte)0xFE); bw.Write((byte)0xB0);
                    bw.Write((byte)(colorTwo ? 2 : 1));
                    colorTwo = !colorTwo;
                    break;
                case StitchCommand.End:
                    FlushTrim();
                    bw.Write((byte)0xFF);
                    return;
            }
        }
        FlushTrim();
        bw.Write((byte)0xFF);
    }

    /// <summary>Miniaturas 48x38 monocromáticas mostradas no visor da máquina: uma geral e uma por cor.</summary>
    private static void WriteIcons(StitchPattern pattern, BinaryWriter bw)
    {
        var blocks = pattern.ColorBlocks().ToList();
        var b = pattern.Bounds();
        double w = Math.Max(1, b.MaxX - b.MinX), h = Math.Max(1, b.MaxY - b.MinY);
        // Área útil interna da moldura: x 5..42, y 3..34
        double scale = Math.Min(37 / w, 31 / h);
        double ox = 5 + (37 - w * scale) / 2, oy = 3 + (31 - h * scale) / 2;

        byte[] NewIcon()
        {
            var icon = new byte[IconStride * IconHeight];
            for (int x = 4; x <= 43; x++) { Set(icon, x, 1); Set(icon, x, IconHeight - 2); }
            for (int y = 2; y <= IconHeight - 3; y++) { Set(icon, 3, y); Set(icon, 44, y); }
            return icon;
        }

        void Plot(byte[] icon, IEnumerable<List<Bordadox.Core.Geometry.Vec2>> runs)
        {
            foreach (var run in runs)
                foreach (var p in run)
                {
                    int px = (int)(ox + (p.X * 10 - b.MinX) * scale);
                    int py = (int)(oy + (p.Y * 10 - b.MinY) * scale);
                    Set(icon, Math.Clamp(px, 5, 42), Math.Clamp(py, 3, 34));
                }
        }

        var all = NewIcon();
        foreach (var blk in blocks) Plot(all, blk.Runs);
        bw.Write(all);

        int colorBlocks = Math.Max(1, pattern.Threads.Count);
        for (int i = 0; i < colorBlocks; i++)
        {
            var icon = NewIcon();
            if (i < blocks.Count) Plot(icon, blocks[i].Runs);
            bw.Write(icon);
        }
    }

    private static void Set(byte[] icon, int x, int y) => icon[y * IconStride + x / 8] |= (byte)(1 << (x % 8));
}
