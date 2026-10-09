using Bordadox.Core.Design;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;
using Bordadox.Core.Text;

namespace Bordadox.Tests;

public class ExploderTests
{
    public ExploderTests() => GlyphService.Provider = new FakeGlyphProvider();

    private static readonly ThreadColor Red = BrotherPalette.Colors[5];
    private static readonly ThreadColor Gray = BrotherPalette.Colors[39];

    private static List<PathNode> Square(double s) =>
        new() { new(new Vec2(0, 0)), new(new Vec2(s, 0)), new(new Vec2(s, s)), new(new Vec2(0, s)) };

    private static Rect2 Union(IEnumerable<EmbObject> objects) => objects.Aggregate(Rect2.Empty, (r, o) => r.Union(o.Bounds()));

    // ---------- Texto ----------

    [Theory]
    [InlineData(TextAlign.Left, 0)]
    [InlineData(TextAlign.Center, 0)]
    [InlineData(TextAlign.Right, 30)]
    public void Text_BecomesOneObjectPerLetter_ExactlyWhereTheyWere(TextAlign align, double rotation)
    {
        var t = new TextObject { Text = "ABC OA", HeightMm = 10, Alignment = align, LetterSpacing = 1.5, Origin = new Vec2(20, 15), FillType = FillType.Satin };
        t.RotationDeg = rotation;
        var parts = Exploder.Explode(t);

        Assert.Equal(5, parts.Count);                                   // o espaço não vira objeto
        Assert.All(parts, p => Assert.IsType<TextObject>(p));
        Assert.Equal(new[] { "A", "B", "C", "O", "A" }, parts.Cast<TextObject>().Select(p => p.Text).ToArray());

        var a = t.Bounds();
        var b = Union(parts);
        Assert.Equal(a.MinX, b.MinX, 6);
        Assert.Equal(a.MaxX, b.MaxX, 6);
        Assert.Equal(a.MinY, b.MinY, 6);
        Assert.Equal(a.MaxY, b.MaxY, 6);
    }

    [Fact]
    public void ExplodedLetters_KeepTheSettings_AndEachOneIsEditableOnItsOwn()
    {
        var t = new TextObject { Text = "HO", HeightMm = 12, FontFamily = "Georgia", Bold = true, Spacing = 0.5, Color = Red, FillType = FillType.Satin, StopBefore = true };
        var parts = Exploder.Explode(t).Cast<TextObject>().ToList();
        Assert.All(parts, p =>
        {
            Assert.Equal("Georgia", p.FontFamily);
            Assert.True(p.Bold);
            Assert.Equal(12, p.HeightMm);
            Assert.Equal(0.5, p.Spacing);
            Assert.Equal(Red, p.Color);
            Assert.NotNull(p.GenerateRuns(null));
            Assert.NotEmpty(p.GenerateRuns(null));
        });
        Assert.NotEqual(parts[0].Id, parts[1].Id);
        Assert.True(parts[0].StopBefore);          // a parada fica na primeira letra
        Assert.False(parts[1].StopBefore);
        parts[1].Origin = new Vec2(parts[1].Origin.X + 10, parts[1].Origin.Y);   // mover uma letra não mexe na outra
        Assert.True(Union(parts).Width > t.Bounds().Width + 9);
    }

    [Fact]
    public void MultiLineText_KeepsEachLineWhereItWas()
    {
        var t = new TextObject { Text = "AB\nCD", HeightMm = 10, Origin = new Vec2(0, 0) };
        var parts = Exploder.Explode(t).Cast<TextObject>().ToList();
        Assert.Equal(4, parts.Count);
        Assert.Equal(t.Bounds().MaxY, Union(parts).MaxY, 6);
        Assert.Equal(t.Bounds().MinY, Union(parts).MinY, 6);
        Assert.True(parts[2].Origin.Y > parts[0].Origin.Y + 10);   // a segunda linha fica abaixo
    }

    [Fact]
    public void SingleLetter_HasNothingToExplode()
    {
        Assert.Empty(Exploder.Explode(new TextObject { Text = "A" }));
        Assert.Empty(Exploder.Explode(new TextObject { Text = "  " }));
        Assert.False(Exploder.CanExplode(new TextObject { Text = "A" }));
    }

    // ---------- Pontos importados ----------

    [Fact]
    public void ImportedStitches_BecomeOneObjectPerRun_WithTheSameStitches()
    {
        var block = new StitchBlockObject
        {
            Name = "IMPERIO — cor 1", Color = Red, MaxStitchMm = 4,
            Runs = new()
            {
                new() { new(0, 0), new(10, 0), new(10, 10) },
                new() { new(30, 0), new(40, 0) },
                new() { new(5, 5) },                       // um ponto só não é um trecho
                new() { new(60, 0), new(70, 5), new(80, 0) },
            },
        };
        var parts = Exploder.Explode(block).Cast<StitchBlockObject>().ToList();
        Assert.Equal(3, parts.Count);
        Assert.All(parts, p => { Assert.Equal(Red, p.Color); Assert.Equal(4, p.MaxStitchMm); Assert.Single(p.Runs); });
        Assert.Equal(block.Runs.Where(r => r.Count >= 2).Sum(r => r.Count), parts.Sum(p => p.Runs[0].Count));
        Assert.Contains("trecho 2", parts[1].Name);
        Assert.Equal(block.Bounds().Width, Union(parts).Width, 6);
    }

    [Fact]
    public void ABlockWithASingleRun_CannotBeExploded()
    {
        Assert.Empty(Exploder.Explode(new StitchBlockObject { Runs = new() { new() { new(0, 0), new(5, 0) } } }));
    }

    // ---------- Aplique e degradê ----------

    [Fact]
    public void Applique_BecomesGuideTackDownAndCover_KeepingTheStops()
    {
        var a = new AppliqueObject { Outline = Square(30), Color = Red, GuideColor = Gray, Name = "Coração" };
        var parts = Exploder.Explode(a).Cast<StitchBlockObject>().ToList();
        Assert.Equal(3, parts.Count);
        Assert.Equal(new[] { Gray, Gray, Red }, parts.Select(p => p.Color).ToArray());
        Assert.Equal(new[] { false, true, true }, parts.Select(p => p.StopBefore).ToArray());
        Assert.Equal("Coração — guia", parts[0].Name);

        var original = new EmbDesign();
        original.Objects.Add(a);
        var exploded = new EmbDesign();
        exploded.Objects.AddRange(parts);
        var po = original.BuildPattern("o");
        var pe = exploded.BuildPattern("e");
        Assert.Equal(po.Stitches.Count(s => s.Command == StitchCommand.ColorChange), pe.Stitches.Count(s => s.Command == StitchCommand.ColorChange));
        Assert.Equal(po.Threads.Count, pe.Threads.Count);
        Assert.InRange(pe.StitchCount, po.StitchCount * 0.95, po.StitchCount * 1.1);
    }

    [Fact]
    public void Gradient_BecomesOneObjectPerColor()
    {
        var f = new FillObject { Outline = Square(30), Effect = FillEffect.Gradient, Color = Red, Color2 = Gray, Angle = 0 };
        var parts = Exploder.Explode(f).Cast<StitchBlockObject>().ToList();
        Assert.Equal(2, parts.Count);
        Assert.Equal(Red, parts[0].Color);
        Assert.Equal(Gray, parts[1].Color);
    }

    [Fact]
    public void SimpleObjects_HaveNothingToExplode()
    {
        Assert.False(Exploder.CanExplode(new FillObject { Outline = Square(20) }));
        Assert.False(Exploder.CanExplode(new RunObject { Nodes = new() { new(new Vec2(0, 0)), new(new Vec2(10, 0)) } }));
        Assert.False(Exploder.CanExplode(new FillObject { Outline = Square(20), Effect = FillEffect.Contour }));
    }

    [Fact]
    public void StopBeforeOnAnyObject_ForcesAStopEvenWithTheSameColor()
    {
        var d = new EmbDesign();
        d.Objects.Add(new RunObject { Nodes = new() { new(new Vec2(0, 0)), new(new Vec2(10, 0)) }, Color = Red });
        d.Objects.Add(new RunObject { Nodes = new() { new(new Vec2(0, 10)), new(new Vec2(10, 10)) }, Color = Red, StopBefore = true });
        var p = d.BuildPattern("t");
        Assert.Equal(1, p.Stitches.Count(s => s.Command == StitchCommand.ColorChange));
        Assert.Equal(2, p.Threads.Count);
    }
}
