using Bordadox.Core.Design;
using Bordadox.Core.Geometry;
using Bordadox.Core.Production;
using Bordadox.Core.Stitches;

namespace Bordadox.Tests;

public class ProductionSheetTests
{
    private static readonly ThreadColor Red = BrotherPalette.Colors[5];
    private static readonly ThreadColor Gray = BrotherPalette.Colors[39];

    private static List<PathNode> Square(double s) =>
        new() { new(new Vec2(0, 0)), new(new Vec2(s, 0)), new(new Vec2(s, s)), new(new Vec2(0, s)) };

    private static EmbDesign Design()
    {
        var d = new EmbDesign { HoopName = "Médio 130 × 180 mm" };
        d.Objects.Add(new FillObject { Outline = Square(30), Color = BrotherPalette.Colors[2], Name = "Fundo" });
        d.Objects.Add(new AppliqueObject { Outline = Square(20), Color = Red, GuideColor = Gray, Name = "Coração" });
        return d;
    }

    [Fact]
    public void Facts_MatchThePattern()
    {
        var d = Design();
        var p = d.BuildPattern("peça");
        var sheet = ProductionSheet.From(p, d, "peça");

        Assert.Equal(p.StitchCount, sheet.Stitches);
        Assert.Equal(p.TrimCount, sheet.Trims);
        Assert.Equal(p.Stitches.Count(s => s.Command == StitchCommand.ColorChange), sheet.ColorChanges);
        Assert.Equal(p.Threads.Count, sheet.Colors.Count);
        Assert.Equal(p.StitchCount, sheet.Colors.Sum(c => c.Stitches));
        Assert.InRange(sheet.WidthMm, 29, 32);
        Assert.InRange(sheet.HeightMm, 29, 32);
        Assert.Equal("Médio 130 × 180 mm", sheet.HoopName);
        Assert.Equal(new[] { 1, 2, 3, 4 }, sheet.Colors.Select(c => c.Order).Take(4).ToArray());
    }

    [Fact]
    public void AppliqueStops_AreExplainedInTheColorList()
    {
        var d = Design();
        var sheet = ProductionSheet.From(d.BuildPattern("t"), d);
        // Azul, guia, guia (parada), vermelho: só a segunda guia leva a observação.
        Assert.Equal(4, sheet.Colors.Count);
        Assert.Equal(new[] { "", "", "Parada", "" }, sheet.Colors.Select(c => c.Note.StartsWith("Parada") ? "Parada" : "").ToArray());
        Assert.Contains(sheet.Objects, o => o.Name == "Coração" && o.Note.Contains("Aplique"));
    }

    [Fact]
    public void ObjectsAreListedInSewingOrder_WithTheirStitches()
    {
        var d = Design();
        var sheet = ProductionSheet.From(d.BuildPattern("t"), d);
        Assert.Equal(new[] { "Fundo", "Coração" }, sheet.Objects.Select(o => o.Name).ToArray());
        Assert.All(sheet.Objects, o => Assert.True(o.Stitches > 100));
    }

    [Fact]
    public void EstimatedTime_GrowsWithStitchesStopsAndTrims()
    {
        var a = new ProductionSheet { Stitches = 7000, ColorChanges = 0, Trims = 0, Speed = 700 };
        Assert.Equal(10, a.EstimatedMinutes, 6);
        var b = new ProductionSheet { Stitches = 7000, ColorChanges = 2, Trims = 10, Speed = 700 };
        Assert.Equal(12, b.EstimatedMinutes, 6);
        Assert.True(new ProductionSheet { Stitches = 7000, Speed = 1000 }.EstimatedMinutes < a.EstimatedMinutes);
    }

    [Fact]
    public void Html_HasEverythingTheOperatorNeeds_AndIsSelfContained()
    {
        var d = Design();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
        var html = ProductionSheet.From(d.BuildPattern("t"), d, "Meu <Projeto> & Cia", png).ToHtml();

        Assert.Contains("Ficha de produção", html);
        Assert.Contains("Meu &lt;Projeto&gt; &amp; Cia", html);          // escapado
        Assert.DoesNotContain("Meu <Projeto>", html);
        Assert.Contains("data:image/png;base64," + Convert.ToBase64String(png), html);
        Assert.Contains("Médio 130 × 180 mm", html);
        Assert.Contains(Red.Name, html);
        Assert.Contains(Gray.Hex.ToUpperInvariant(), html.ToUpperInvariant());
        Assert.Contains("Coração", html);
        Assert.Contains("Aplique:", html);                               // aviso sobre as paradas
        Assert.Contains("Tempo estimado", html);
        Assert.DoesNotContain("http://", html);                          // nada externo
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void WithoutAnApplique_ThereIsNoApliqueNote_AndWithoutPreviewNoImage()
    {
        var d = new EmbDesign();
        d.Objects.Add(new FillObject { Outline = Square(20), Color = Red });
        var html = ProductionSheet.From(d.BuildPattern("t"), d).ToHtml();
        Assert.DoesNotContain("Aplique:", html);
        Assert.DoesNotContain("<img", html);
    }
}
