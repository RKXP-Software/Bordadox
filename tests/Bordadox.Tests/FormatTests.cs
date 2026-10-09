using Bordadox.Core.Formats;
using Bordadox.Core.Machine;
using Bordadox.Core.Stitches;

namespace Bordadox.Tests;

public class FormatTests
{
    /// <summary>Quadrado 20 mm em ponto corrido (cor 1), corte, e um zigue-zague (cor 2) com um ponto longo.</summary>
    private static StitchPattern Sample()
    {
        var p = new StitchPattern { Name = "teste" };
        p.Threads.Add(BrotherPalette.Colors[5]);
        p.Threads.Add(BrotherPalette.Colors[2]);
        p.Add(0, 0, StitchCommand.Jump);
        foreach (var (x, y) in new[] { (100, 0), (200, 0), (200, 100), (200, 200), (100, 200), (0, 200), (0, 100), (0, 0) })
            p.Add(x, y, StitchCommand.Stitch);
        p.Add(0, 0, StitchCommand.Trim);
        p.Add(0, 300, StitchCommand.ColorChange);
        p.Add(0, 300, StitchCommand.Jump);
        for (int i = 1; i <= 20; i++) p.Add(i * 10, 300 + (i % 2) * 40, StitchCommand.Stitch);
        p.Add(-150, 300, StitchCommand.Stitch); // ponto de 35 mm: precisa ser dividido
        p.Add(-150, 300, StitchCommand.End);
        return p;
    }

    private static StitchPattern RoundTrip(IEmbroideryFormat format, StitchPattern p)
    {
        using var ms = new MemoryStream();
        format.Write(p, ms);
        ms.Position = 0;
        return format.Read(ms);
    }

    private static List<(int X, int Y)> NeedlePoints(StitchPattern p) =>
        p.Stitches.Where(s => s.Command == StitchCommand.Stitch).Select(s => (s.X, s.Y)).ToList();

    [Theory]
    [InlineData("pes")]
    [InlineData("dst")]
    public void RoundTrip_PreservesShapeAndCommands(string ext)
    {
        var format = EmbroideryFormats.All.Single(f => f.Extension == ext);
        var original = Sample();
        var back = RoundTrip(format, original);

        // Gravação centraliza o desenho; compara após centralizar o original também.
        var expected = original.Centered().WithMaxMove(ext == "dst" ? 121 : 2000);
        Assert.Equal(NeedlePoints(expected), NeedlePoints(back));
        Assert.Equal(1, back.Stitches.Count(s => s.Command == StitchCommand.ColorChange));
        // DST não tem comando de corte: 3+ saltos seguidos são lidos como corte,
        // então saltos longos divididos também geram cortes (inofensivo).
        int trims = back.Stitches.Count(s => s.Command == StitchCommand.Trim);
        if (ext == "pes") Assert.Equal(1, trims); else Assert.True(trims >= 1);
        Assert.Equal(StitchCommand.End, back.Stitches[^1].Command);
    }

    [Fact]
    public void Pes_PreservesThreadColors()
    {
        var back = RoundTrip(new PesFormat(), Sample());
        Assert.Equal(new[] { "5", "2" }, back.Threads.Select(t => t.Code));
    }

    [Fact]
    public void Pes_HeaderLayoutMatchesSpec()
    {
        using var ms = new MemoryStream();
        new PesFormat().Write(Sample(), ms);
        var bytes = ms.ToArray();
        int pec = BitConverter.ToInt32(bytes, 8);

        Assert.Equal("#PES0001", System.Text.Encoding.ASCII.GetString(bytes, 0, 8));
        Assert.Equal(22, pec);
        Assert.Equal("LA:", System.Text.Encoding.ASCII.GetString(bytes, pec, 3));
        Assert.Equal(6, bytes[pec + 34]);   // largura do ícone / 8
        Assert.Equal(38, bytes[pec + 35]);  // altura do ícone
        Assert.Equal(1, bytes[pec + 48]);   // número de cores - 1
        Assert.Equal(new byte[] { 0x31, 0xFF, 0xF0 }, bytes[(pec + 517)..(pec + 520)]);

        // Offset dos gráficos aponta para o fim do bloco: 2 ícones de cor + 1 geral, 228 bytes cada.
        int blockLen = bytes[pec + 514] | bytes[pec + 515] << 8 | bytes[pec + 516] << 16;
        Assert.Equal(bytes.Length, pec + 512 + blockLen + 3 * 228);
        Assert.Equal(0xFF, bytes[pec + 512 + blockLen - 1]); // fim dos pontos
    }

    [Fact]
    public void Pes_DesignIsCentered()
    {
        var back = RoundTrip(new PesFormat(), Sample());
        var b = back.Bounds();
        Assert.InRange(b.MinX + b.MaxX, -1, 1);
        Assert.InRange(b.MinY + b.MaxY, -1, 1);
    }

    [Fact]
    public void WithMaxMove_SplitsLongStitches()
    {
        var split = Sample().WithMaxMove(121);
        int x = 0, y = 0;
        foreach (var s in split.Stitches.Where(s => s.Command is StitchCommand.Stitch or StitchCommand.Jump))
        {
            Assert.True(Math.Abs(s.X - x) <= 121 && Math.Abs(s.Y - y) <= 121);
            x = s.X; y = s.Y;
        }
    }

    [Theory]
    [InlineData(90, 90, "Pequeno 100 × 100 mm")]
    [InlineData(120, 150, "Médio 130 × 180 mm")]
    [InlineData(150, 250, "Grande 160 × 260 mm")]
    [InlineData(170, 100, null)]
    public void Hoops_SmallestFitting(double w, double h, string? expected) =>
        Assert.Equal(expected, Hoops.SmallestFitting(w, h)?.Name);
}
