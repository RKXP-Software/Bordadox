using System.Text;
using Bordadox.Core.Digitize;
using Bordadox.Core.Stitches;

namespace Bordadox.Core.Formats;

/// <summary>Paleta de 78 linhas Janome usada nos arquivos JEF (índices de 1 a 78).</summary>
public static class JefPalette
{
    public static readonly IReadOnlyList<ThreadColor> Colors = Build();
    private static readonly Lab[] LabColors = Colors.Select(c => ColorSpace.ToLab(c.R, c.G, c.B)).ToArray();

    private static List<ThreadColor> Build()
    {
        var table = new (string Hex, string Name)[]
        {
            ("000000", "Black"), ("FFFFFF", "White"), ("FFFF17", "Yellow"), ("FF6600", "Orange"), ("2F5933", "Olive Green"),
            ("237336", "Green"), ("65C2C8", "Sky"), ("AB5A96", "Purple"), ("F669A0", "Pink"), ("FF0000", "Red"),
            ("B1704E", "Brown"), ("0B2F84", "Blue"), ("E4C35D", "Gold"), ("481A05", "Dark Brown"), ("AC9CC7", "Pale Violet"),
            ("FCF294", "Pale Yellow"), ("F999B7", "Pale Pink"), ("FAB381", "Peach"), ("C9A480", "Beige"), ("970533", "Wine Red"),
            ("A0B8CC", "Pale Sky"), ("7FC21C", "Yellow Green"), ("E5E5E5", "Silver Gray"), ("889B9B", "Gray"), ("98D6BD", "Pale Aqua"),
            ("B2E1E3", "Baby Blue"), ("368BA0", "Powder Blue"), ("4F83AB", "Bright Blue"), ("386A91", "Slate Blue"), ("071650", "Navy Blue"),
            ("F999A2", "Salmon Pink"), ("F9676B", "Coral"), ("E3311F", "Burnt Orange"), ("E2A188", "Cinnamon"), ("B59474", "Umber"),
            ("E4CF99", "Blond"), ("FFCB00", "Sunflower"), ("E1ADD4", "Orchid Pink"), ("C3007E", "Peony Purple"), ("80004B", "Burgundy"),
            ("540571", "Royal Purple"), ("B10525", "Cardinal Red"), ("CAE0C0", "Opal Green"), ("899856", "Moss Green"), ("5C941A", "Meadow Green"),
            ("003114", "Dark Green"), ("5DAE94", "Aquamarine"), ("4CBF8F", "Emerald Green"), ("007772", "Peacock Green"), ("595B61", "Dark Gray"),
            ("FFFFF2", "Ivory White"), ("B15818", "Hazel"), ("CB8A07", "Toast"), ("986C80", "Salmon"), ("98692D", "Cocoa Brown"),
            ("4D3419", "Sienna"), ("4C330B", "Sepia"), ("33200A", "Dark Sepia"), ("523A97", "Violet Blue"), ("0D217E", "Blue Ink"),
            ("1E77AC", "Sola Blue"), ("B2DD53", "Green Dust"), ("F33689", "Crimson"), ("DE649E", "Floral Pink"), ("984161", "Wine"),
            ("4C5612", "Olive Drab"), ("4C881F", "Meadow"), ("E4DE79", "Mustard"), ("CB8A1A", "Yellow Ocher"), ("CBA21C", "Old Gold"),
            ("FF9805", "Honey Dew"), ("FCB257", "Tangerine"), ("FFE505", "Canary Yellow"), ("F0331F", "Vermilion"), ("1A842D", "Bright Green"),
            ("386CAE", "Ocean Blue"), ("E3C4B4", "Beige Gray"), ("E3AC81", "Bamboo"),
        };
        return table.Select((t, i) => new ThreadColor(
            Convert.ToByte(t.Hex[..2], 16), Convert.ToByte(t.Hex[2..4], 16), Convert.ToByte(t.Hex[4..], 16), t.Name, (i + 1).ToString())).ToList();
    }

    /// <summary>Índice JEF (1 a 78) da linha mais próxima, por CIEDE2000; <paramref name="skip"/> exclui um índice (para a segunda mais próxima).</summary>
    public static int NearestIndex(ThreadColor c, int skip = -1)
    {
        var lab = ColorSpace.ToLab(c.R, c.G, c.B);
        int best = 1;
        double bestD = double.MaxValue;
        for (int i = 0; i < LabColors.Length; i++)
        {
            if (i + 1 == skip) continue;
            double d = DeltaE2000.Between(lab, LabColors[i]);
            if (d < bestD) { bestD = d; best = i + 1; }
        }
        return best;
    }
}

/// <summary>
/// Formato Janome JEF: cabeçalho de 116 bytes (deslocamento, data, número de linhas e de pontos, bastidor e folgas até as bordas de
/// 4 bastidores), a lista de linhas (índices da paleta Janome) e registros (dx, -dy) em 0,1 mm. Comandos: 0x80 0x01 = troca de linha,
/// 0x80 0x02 = salto, 0x80 0x10 = fim. O corte é gravado como três saltos nulos. Deslocamento máximo de ±127 por registro.
/// Escrito a partir da especificação do formato; ainda não validado num software ou máquina Janome.
/// </summary>
public sealed class JefFormat : IEmbroideryFormat
{
    public string Description => "Janome JEF";
    public string Extension => "jef";
    public bool CanRead => true;
    public bool CanWrite => true;

    private const int MaxMove = 127;
    private const int HeaderSize = 0x74;
    private const int TrimRecords = 3;

    public StitchPattern Read(Stream stream)
    {
        using var br = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        int offset = br.ReadInt32();
        stream.Seek(24, SeekOrigin.Begin);
        int colorCount = br.ReadInt32();
        if (colorCount < 0 || colorCount > 1000) throw new InvalidDataException("Arquivo JEF inválido (número de linhas).");

        stream.Seek(HeaderSize, SeekOrigin.Begin);
        var palette = new List<int>();
        for (int i = 0; i < colorCount; i++) palette.Add(br.ReadInt32());

        var pattern = new StitchPattern();
        foreach (var idx in palette)
            if (idx >= 1 && idx <= JefPalette.Colors.Count) pattern.Threads.Add(JefPalette.Colors[idx - 1]);

        stream.Seek(offset, SeekOrigin.Begin);
        int x = 0, y = 0, colorIndex = 0, jumpRun = 0, runStart = 0;
        var rec = new byte[2];
        while (stream.Read(rec, 0, 2) == 2)
        {
            if (rec[0] != 0x80)
            {
                x += (sbyte)rec[0];
                y -= (sbyte)rec[1];
                pattern.Add(x, y, StitchCommand.Stitch);
                jumpRun = 0;
                continue;
            }

            if (rec[1] == 0x10) break;
            var arg = new byte[2];
            if (stream.Read(arg, 0, 2) != 2) break;
            x += (sbyte)arg[0];
            y -= (sbyte)arg[1];

            if (rec[1] == 0x02)
            {
                if (jumpRun == 0) runStart = pattern.Stitches.Count;
                jumpRun++;
                pattern.Add(x, y, StitchCommand.Jump);
                // Convenção Janome: 3 ou mais saltos seguidos são um corte de linha (como no DST).
                if (jumpRun == TrimRecords)
                {
                    var prev = runStart > 0 ? pattern.Stitches[runStart - 1] : new Stitch(0, 0, StitchCommand.Stitch);
                    pattern.Stitches.Insert(runStart, new Stitch(prev.X, prev.Y, StitchCommand.Trim));
                }
            }
            else if (rec[1] == 0x01)
            {
                colorIndex++;
                bool isStop = colorIndex < palette.Count && palette[colorIndex] == 0;   // índice 0 na lista = parada, não troca de linha
                pattern.Add(x, y, isStop ? StitchCommand.Stop : StitchCommand.ColorChange);
                jumpRun = 0;
            }
            else break;
        }
        pattern.Add(x, y, StitchCommand.End);
        return pattern;
    }

    public void Write(StitchPattern source, Stream stream)
    {
        var pattern = source.Centered().WithMaxMove(MaxMove);
        pattern.FixThreadCount();

        // Lista de linhas: uma por bloco de cor (e uma por parada). Duas linhas seguidas diferentes que caem no mesmo
        // índice da paleta usam o segundo mais próximo para a máquina distinguir uma da outra.
        var palette = new List<int>();
        int threadPos = 0, last = 0;
        bool toggled = false;
        void NextThread()
        {
            var t = pattern.Threads[Math.Min(threadPos, pattern.Threads.Count - 1)];
            int idx = JefPalette.NearestIndex(t);
            if (threadPos > 0 && !SameColor(t, pattern.Threads[threadPos - 1]) && idx == last) idx = JefPalette.NearestIndex(t, skip: idx);
            palette.Add(idx);
            last = idx;
            threadPos++;
        }
        NextThread();
        foreach (var s in pattern.Stitches)
        {
            if (s.Command == StitchCommand.ColorChange) { toggled = false; NextThread(); }
            else if (s.Command == StitchCommand.Stop) { toggled = !toggled; palette.Add(toggled ? 0 : last); }
        }

        int points = 1;
        foreach (var s in pattern.Stitches)
        {
            points += s.Command switch
            {
                StitchCommand.Stitch => 1,
                StitchCommand.Jump => 2,
                StitchCommand.Trim => 2 * TrimRecords,
                StitchCommand.ColorChange or StitchCommand.Stop => 2,
                _ => 0,
            };
        }

        var b = pattern.Bounds();
        int width = b.MaxX - b.MinX, height = b.MaxY - b.MinY;
        int halfW = (int)Math.Round(width / 2.0), halfH = (int)Math.Round(height / 2.0);

        using var bw = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        bw.Write(HeaderSize + palette.Count * 8);
        bw.Write(0x14);
        bw.Write(Encoding.ASCII.GetBytes(DateTime.Now.ToString("yyyyMMddHHmmss")));
        bw.Write((byte)0); bw.Write((byte)0);
        bw.Write(palette.Count);
        bw.Write(points);
        bw.Write(HoopCode(width, height));
        bw.Write(halfW); bw.Write(halfH); bw.Write(halfW); bw.Write(halfH);
        foreach (var (hx, hy) in new[] { (550, 550), (250, 250), (700, 1000), (700, 1000) })   // 110x110, 50x50, 140x200 e "personalizado"
        {
            int ex = hx - halfW, ey = hy - halfH;
            if (Math.Min(ex, ey) < 0) { for (int i = 0; i < 4; i++) bw.Write(-1); }
            else { bw.Write(ex); bw.Write(ey); bw.Write(ex); bw.Write(ey); }
        }
        foreach (var idx in palette) bw.Write(idx);
        foreach (var _ in palette) bw.Write(0x0D);

        int x = 0, y = 0;
        foreach (var s in pattern.Stitches)
        {
            int dx = s.X - x, dy = s.Y - y;
            x = s.X; y = s.Y;
            switch (s.Command)
            {
                case StitchCommand.Stitch:
                    bw.Write((sbyte)dx); bw.Write((sbyte)-dy);
                    break;
                case StitchCommand.Jump:
                    bw.Write((byte)0x80); bw.Write((byte)0x02); bw.Write((sbyte)dx); bw.Write((sbyte)-dy);
                    break;
                case StitchCommand.ColorChange:
                case StitchCommand.Stop:
                    bw.Write((byte)0x80); bw.Write((byte)0x01); bw.Write((sbyte)dx); bw.Write((sbyte)-dy);
                    break;
                case StitchCommand.Trim:
                    for (int i = 0; i < TrimRecords; i++) bw.Write(new byte[] { 0x80, 0x02, 0x00, 0x00 });
                    break;
            }
        }
        bw.Write((byte)0x80); bw.Write((byte)0x10);
    }

    private static bool SameColor(ThreadColor a, ThreadColor b) => a.R == b.R && a.G == b.G && a.B == b.B;

    /// <summary>Código do bastidor Janome para o tamanho do desenho (0,1 mm): 0 = 110x110, 1 = 50x50, 2 = 140x200, 3 = 126x110, 4 = 200x200.</summary>
    public static int HoopCode(int width, int height)
    {
        if (width < 500 && height < 500) return 1;
        if (width < 1260 && height < 1100) return 3;
        if (width < 1400 && height < 2000) return 2;
        if (width < 2000 && height < 2000) return 4;
        return 0;
    }
}
