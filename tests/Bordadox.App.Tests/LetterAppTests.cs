using System.Diagnostics;
using Bordadox.App.Text;
using Bordadox.Core.Design;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;
using Bordadox.Core.Text;
using Xunit.Abstractions;

namespace Bordadox.App.Tests;

/// <summary>Letras de fontes reais do Windows: satin em colunas, kerning e tempo de geração.</summary>
public class LetterAppTests(ITestOutputHelper output)
{
    private static TextObject Make(string text, string font, bool bold = false, double height = 12) => new()
    {
        Text = text, FontFamily = font, Bold = bold, HeightMm = height, FillType = FillType.Satin, Color = BrotherPalette.Colors[5],
    };

    private static StitchPattern Pattern(TextObject t)
    {
        var d = new EmbDesign();
        d.Objects.Add(t);
        return d.BuildPattern("t");
    }

    [Theory]
    [InlineData("Arial", true)]
    [InlineData("Times New Roman", false)]
    [InlineData("Segoe UI", false)]
    public void RealFonts_ColumnSatin_StaysInsideTheLettersAndCutsNoMoreThanBefore(string font, bool bold)
    {
        GlyphService.Provider = new WpfGlyphProvider();
        var columns = Make("Rosa AVATAR 27", font, bold);
        var lines = Make("Rosa AVATAR 27", font, bold);
        lines.SatinColumns = false;

        var box = columns.Bounds().Inflate(0.6);
        foreach (var p in columns.GenerateRuns(null).SelectMany(r => r))
            Assert.True(box.Contains(p), $"{p} fora do texto");

        var pc = Pattern(columns);
        var pl = Pattern(lines);
        output.WriteLine($"{font}: colunas {pc.StitchCount} pontos, {pc.TrimCount} cortes; linhas {pl.StitchCount} pontos, {pl.TrimCount} cortes");
        Assert.InRange(pc.StitchCount, pl.StitchCount * 0.5, pl.StitchCount * 2.0);
        Assert.True(pc.TrimCount <= pl.TrimCount + 3, "as colunas não podem gerar bem mais cortes");
    }

    [Fact]
    public void Kerning_MakesRealTextNarrower_WhenTheFontHasIt()
    {
        GlyphService.Provider = new WpfGlyphProvider();
        var kerned = Make("AVATAR To", "Arial", height: 14);
        var plain = Make("AVATAR To", "Arial", height: 14);
        plain.Kerning = false;
        Assert.True(kerned.Bounds().Width < plain.Bounds().Width - 0.5, $"{kerned.Bounds().Width:0.0} x {plain.Bounds().Width:0.0}");
    }

    [Fact]
    public void LongText_IsGeneratedInReasonableTime_AndMovingItReusesTheColumns()
    {
        GlyphService.Provider = new WpfGlyphProvider();
        var t = Make("Bordados Personalizados Rosa Atelier 2026", "Segoe UI", height: 10);

        var sw = Stopwatch.StartNew();
        var first = t.GenerateRuns(null);
        var firstMs = sw.ElapsedMilliseconds;
        t.Origin = new Vec2(10, 3);
        sw.Restart();
        var second = t.GenerateRuns(null);
        var secondMs = sw.ElapsedMilliseconds;
        output.WriteLine($"primeira geração {firstMs} ms; depois de mover {secondMs} ms; {first.Sum(r => r.Count)} pontos");

        Assert.Equal(first.Sum(r => r.Count), second.Sum(r => r.Count));
        Assert.True(firstMs < 8000, $"primeira geração demorou {firstMs} ms");
        Assert.True(secondMs < 1500, $"mover o texto demorou {secondMs} ms: as colunas não estão sendo reaproveitadas");
    }
}
