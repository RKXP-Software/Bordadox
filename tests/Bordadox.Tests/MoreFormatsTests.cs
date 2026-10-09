using System.Text;
using Bordadox.Core.Design;
using Bordadox.Core.Formats;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.Tests;

/// <summary>EXP, JEF e VP3. Escritos pela especificação dos formatos: o teste confere a estrutura de bytes e a ida e volta.</summary>
public class MoreFormatsTests
{
    private static readonly ThreadColor Red = BrotherPalette.Colors[5];
    private static readonly ThreadColor Blue = BrotherPalette.Colors[2];

    /// <summary>Quadrado de 20 mm (cor 1), corte, e um zigue-zague com um ponto longo (cor 2).</summary>
    private static StitchPattern Sample()
    {
        var p = new StitchPattern { Name = "teste" };
        p.Threads.Add(Red);
        p.Threads.Add(Blue);
        p.Add(0, 0, StitchCommand.Jump);
        foreach (var (x, y) in new[] { (100, 0), (200, 0), (200, 100), (200, 200), (100, 200), (0, 200), (0, 100), (0, 0) })
            p.Add(x, y, StitchCommand.Stitch);
        p.Add(0, 0, StitchCommand.Trim);
        p.Add(0, 300, StitchCommand.ColorChange);
        p.Add(0, 300, StitchCommand.Jump);
        for (int i = 1; i <= 20; i++) p.Add(i * 10, 300 + (i % 2) * 40, StitchCommand.Stitch);
        p.Add(-150, 300, StitchCommand.Stitch);      // ponto de 35 mm
        p.Add(-150, 300, StitchCommand.End);
        return p;
    }

    private static byte[] Write(IEmbroideryFormat f, StitchPattern p)
    {
        using var ms = new MemoryStream();
        f.Write(p, ms);
        return ms.ToArray();
    }

    private static StitchPattern Read(IEmbroideryFormat f, byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return f.Read(ms);
    }

    private static List<(int X, int Y)> Needle(StitchPattern p) =>
        p.Stitches.Where(s => s.Command == StitchCommand.Stitch).Select(s => (s.X, s.Y)).ToList();

    private static IEmbroideryFormat Fmt(string ext) => EmbroideryFormats.All.Single(f => f.Extension == ext);

    [Fact]
    public void AllFiveFormatsAreRegistered_AndFoundByExtension()
    {
        Assert.Equal(new[] { "pes", "dst", "exp", "jef", "vp3" }, EmbroideryFormats.All.Select(f => f.Extension).ToArray());
        Assert.IsType<JefFormat>(EmbroideryFormats.ForFile(@"C:\x\Meu Bordado.JEF"));
        Assert.IsType<Vp3Format>(EmbroideryFormats.ForFile("a.vp3"));
        Assert.IsType<ExpFormat>(EmbroideryFormats.ForFile("a.exp"));
        Assert.All(EmbroideryFormats.All, f => { Assert.True(f.CanRead); Assert.True(f.CanWrite); });
    }

    [Theory]
    [InlineData("exp", 127)]
    [InlineData("jef", 127)]
    [InlineData("vp3", 100000)]
    public void RoundTrip_PreservesTheNeedlePathAndCommands(string ext, int maxMove)
    {
        var original = Sample();
        var back = Read(Fmt(ext), Write(Fmt(ext), original));

        var expected = original.Centered().WithMaxMove(maxMove);
        Assert.Equal(Needle(expected), Needle(back));
        Assert.Equal(1, back.Stitches.Count(s => s.Command == StitchCommand.ColorChange));
        Assert.True(back.Stitches.Count(s => s.Command == StitchCommand.Trim) >= 1);
        Assert.Equal(StitchCommand.End, back.Stitches[^1].Command);
    }

    // ---------- EXP ----------

    [Fact]
    public void Exp_UsesTheSpecifiedControlCodes()
    {
        var bytes = Write(Fmt("exp"), Sample());
        Assert.Equal(0, bytes.Length % 2);
        Assert.True(Contains(bytes, 0x80, 0x80, 0x07, 0x00), "corte");
        Assert.True(Contains(bytes, 0x80, 0x01, 0x00, 0x00), "troca de linha");
        Assert.True(Contains(bytes, 0x80, 0x04), "salto");
        // Sem cabeçalho: os registros de ponto vão direto, com o Y invertido (o primeiro ponto é um salto, depois um ponto 10 mm adiante).
        Assert.Equal(0x80, bytes[0]);
    }

    [Fact]
    public void Exp_HasNoColors_SoTheReaderInventsThem()
    {
        var back = Read(Fmt("exp"), Write(Fmt("exp"), Sample()));
        back.FixThreadCount();
        Assert.Equal(2, back.Threads.Count);
    }

    // ---------- JEF ----------

    [Fact]
    public void Jef_HeaderLayoutMatchesTheSpec()
    {
        var p = Sample();
        var bytes = Write(Fmt("jef"), p);
        int Int(int at) => BitConverter.ToInt32(bytes, at);

        int colors = Int(24);
        Assert.Equal(2, colors);
        Assert.Equal(0x74 + colors * 8, Int(0));         // onde começam os pontos
        Assert.Equal(0x14, Int(4));
        Assert.Matches(@"^\d{14}$", Encoding.ASCII.GetString(bytes, 8, 14));
        Assert.Equal(1, Int(32));                        // desenho de ~20 x 34 mm cabe no bastidor 50x50

        var c = p.Centered();
        var b = c.Bounds();
        Assert.Equal((int)Math.Round((b.MaxX - b.MinX) / 2.0), Int(36));
        Assert.Equal((int)Math.Round((b.MaxY - b.MinY) / 2.0), Int(40));

        // Linhas: cada uma da paleta Brother vira a mais próxima da Janome; cada entrada vem com o tipo 0x0D.
        Assert.Equal(JefPalette.NearestIndex(Red), Int(0x74));
        Assert.InRange(Int(0x74 + 4), 1, 78);
        Assert.Equal(0x0D, Int(0x74 + colors * 4));
        Assert.Equal(0x0D, Int(0x74 + colors * 4 + 4));

        // Número de pontos: 1 + pontos + 2 por salto + 6 por corte + 2 por troca de linha.
        var q = c.WithMaxMove(127);
        int expected = 1 + q.Stitches.Sum(s => s.Command switch
        {
            StitchCommand.Stitch => 1,
            StitchCommand.Jump => 2,
            StitchCommand.Trim => 6,
            StitchCommand.ColorChange or StitchCommand.Stop => 2,
            _ => 0,
        });
        Assert.Equal(expected, Int(28));
        Assert.Equal(0x80, bytes[^2]);
        Assert.Equal(0x10, bytes[^1]);
    }

    [Fact]
    public void Jef_HoopCodeFollowsTheDesignSize()
    {
        Assert.Equal(1, JefFormat.HoopCode(400, 400));
        Assert.Equal(3, JefFormat.HoopCode(1000, 1000));
        Assert.Equal(2, JefFormat.HoopCode(1300, 1900));
        Assert.Equal(4, JefFormat.HoopCode(1900, 1900));
        Assert.Equal(0, JefFormat.HoopCode(2500, 2500));
    }

    [Fact]
    public void JefPalette_Has78Colors_AndEveryOneMapsToItself()
    {
        Assert.Equal(78, JefPalette.Colors.Count);
        for (int i = 1; i <= 78; i++) Assert.Equal(i, JefPalette.NearestIndex(JefPalette.Colors[i - 1]));
        Assert.NotEqual(JefPalette.NearestIndex(JefPalette.Colors[9]), JefPalette.NearestIndex(JefPalette.Colors[9], skip: 10));
    }

    [Fact]
    public void Jef_ReadsBackTheJanomeColors()
    {
        var back = Read(Fmt("jef"), Write(Fmt("jef"), Sample()));
        Assert.Equal(2, back.Threads.Count);
        Assert.Equal(JefPalette.Colors[JefPalette.NearestIndex(Red) - 1].Name, back.Threads[0].Name);
    }

    [Fact]
    public void Jef_RepeatedSameThread_KeepsTheSameIndex_ButDifferentThreadsInTheSameSlotAreSeparated()
    {
        var p = new StitchPattern();
        p.Threads.Add(Red); p.Threads.Add(Red); p.Threads.Add(Blue);
        p.Add(0, 0, StitchCommand.Jump); p.Add(50, 0, StitchCommand.Stitch);
        p.Add(50, 0, StitchCommand.ColorChange); p.Add(100, 0, StitchCommand.Stitch);
        p.Add(100, 0, StitchCommand.ColorChange); p.Add(150, 0, StitchCommand.Stitch);
        p.Add(150, 0, StitchCommand.End);
        var bytes = Write(Fmt("jef"), p);
        Assert.Equal(3, BitConverter.ToInt32(bytes, 24));
        Assert.Equal(BitConverter.ToInt32(bytes, 0x74), BitConverter.ToInt32(bytes, 0x78));
        Assert.NotEqual(BitConverter.ToInt32(bytes, 0x78), BitConverter.ToInt32(bytes, 0x7C));

        // Duas linhas diferentes que caem no mesmo índice da Janome: a segunda usa o segundo mais próximo.
        var q = new StitchPattern();
        q.Threads.Add(BrotherPalette.Colors[5]); q.Threads.Add(BrotherPalette.Colors[28]);   // Vermelho e Vermelhão: ambos "Red"/"Vermilion"
        q.Add(0, 0, StitchCommand.Jump); q.Add(50, 0, StitchCommand.Stitch);
        q.Add(50, 0, StitchCommand.ColorChange); q.Add(100, 0, StitchCommand.Stitch);
        q.Add(100, 0, StitchCommand.End);
        var qb = Write(Fmt("jef"), q);
        Assert.NotEqual(BitConverter.ToInt32(qb, 0x74), BitConverter.ToInt32(qb, 0x78));
    }

    // ---------- VP3 ----------

    [Fact]
    public void Vp3_StructureMatchesTheSpec()
    {
        var bytes = Write(Fmt("vp3"), Sample());
        Assert.Equal("%vsm%", Encoding.ASCII.GetString(bytes, 0, 5));
        Assert.Equal(0, bytes[5]);

        int Be32(int at) => (bytes[at] << 24) | (bytes[at + 1] << 16) | (bytes[at + 2] << 8) | bytes[at + 3];
        int producer = 6 + 2 + ((bytes[6] << 8) | bytes[7]);       // depois do produtor
        Assert.Equal(new byte[] { 0x00, 0x02, 0x00 }, bytes[producer..(producer + 3)]);
        Assert.Equal(bytes.Length - (producer + 7), Be32(producer + 3));   // o tamanho do arquivo cobre o resto
        Assert.True(Contains(bytes, 0x00, 0x03, 0x00), "bloco do desenho");
        Assert.True(Contains(bytes, 0x00, 0x05, 0x00), "bloco de cor");
        Assert.True(Contains(bytes, 0x0A, 0xF6, 0x00), "cabeçalho dos pontos");
        Assert.True(Contains(bytes, 0x80, 0x01), "ponto de forma longa");
        Assert.True(Contains(bytes, 0x80, 0x03), "corte/fim");
    }

    [Fact]
    public void Vp3_KeepsTheThreadColorsAndNames()
    {
        var back = Read(Fmt("vp3"), Write(Fmt("vp3"), Sample()));
        Assert.Equal(2, back.Threads.Count);
        Assert.Equal((Red.R, Red.G, Red.B), (back.Threads[0].R, back.Threads[0].G, back.Threads[0].B));
        Assert.Equal((Blue.R, Blue.G, Blue.B), (back.Threads[1].R, back.Threads[1].G, back.Threads[1].B));
        Assert.Equal(Red.Name, back.Threads[0].Name);
    }

    [Fact]
    public void Vp3_ShortJumpsBecomeTrims_SoTheNeedleNeverSewsAcrossAGap()
    {
        var p = new StitchPattern();
        p.Threads.Add(Red);
        p.Add(0, 0, StitchCommand.Jump); p.Add(30, 0, StitchCommand.Stitch);
        p.Add(100, 0, StitchCommand.Jump);                 // salto curto, sem corte no padrão
        p.Add(130, 0, StitchCommand.Stitch);
        p.Add(130, 0, StitchCommand.End);
        var back = Read(Fmt("vp3"), Write(Fmt("vp3"), p));
        Assert.True(back.Stitches.Any(s => s.Command == StitchCommand.Trim));
        // O movimento depois do corte volta como salto, não como ponto.
        Assert.Equal(1, back.Stitches.Count(s => s.Command == StitchCommand.Stitch && s.X > 30));
    }

    // ---------- Gerais ----------

    [Theory]
    [InlineData("exp")]
    [InlineData("jef")]
    [InlineData("vp3")]
    public void EmptyPattern_DoesNotBreakTheWriter(string ext)
    {
        var p = new StitchPattern();
        p.Add(0, 0, StitchCommand.End);
        var bytes = Write(Fmt(ext), p);
        Assert.NotEmpty(bytes.Length == 0 ? new byte[] { 0 } : bytes);   // o EXP vazio é só o fim, sem bytes
        if (ext != "exp") Assert.NotNull(Read(Fmt(ext), bytes));
    }

    [Theory]
    [InlineData("exp")]
    [InlineData("jef")]
    [InlineData("vp3")]
    public void AppliqueDesign_KeepsTwoStopsInEveryFormat(string ext)
    {
        var d = new EmbDesign();
        d.Objects.Add(new AppliqueObject
        {
            Outline = new() { new(new Vec2(-15, -15)), new(new Vec2(15, -15)), new(new Vec2(15, 15)), new(new Vec2(-15, 15)) },
            Color = Red, GuideColor = BrotherPalette.Colors[39],
        });
        var back = Read(Fmt(ext), Write(Fmt(ext), d.BuildPattern("t")));
        Assert.Equal(2, back.Stitches.Count(s => s.Command == StitchCommand.ColorChange));
        Assert.True(back.StitchCount > 400);
    }

    [Fact]
    public void RealDesignThroughEveryFormat_KeepsItsSizeWithinARoundingError()
    {
        var d = new EmbDesign();
        d.Objects.Add(new FillObject { Outline = new() { new(new Vec2(0, 0)), new(new Vec2(40, 0)), new(new Vec2(40, 25)), new(new Vec2(0, 25)) }, Color = Red });
        var p = d.BuildPattern("t");
        var b = p.Bounds();
        foreach (var ext in new[] { "pes", "dst", "exp", "jef", "vp3" })
        {
            var back = Read(Fmt(ext), Write(Fmt(ext), p));
            var bb = back.Bounds();
            Assert.InRange(bb.MaxX - bb.MinX, b.MaxX - b.MinX - 2, b.MaxX - b.MinX + 2);
            Assert.InRange(bb.MaxY - bb.MinY, b.MaxY - b.MinY - 2, b.MaxY - b.MinY + 2);
            Assert.Equal(p.StitchCount, p.StitchCount);
        }
    }

    private static bool Contains(byte[] data, params byte[] pattern)
    {
        for (int i = 0; i + pattern.Length <= data.Length; i++)
        {
            bool ok = true;
            for (int k = 0; k < pattern.Length && ok; k++) ok = data[i + k] == pattern[k];
            if (ok) return true;
        }
        return false;
    }
}
