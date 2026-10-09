using Bordadox.Core.Design;
using Bordadox.Core.Generators;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.Tests;

public class FillEffectTests
{
    private static readonly ThreadColor Red = BrotherPalette.Colors[5];
    private static readonly ThreadColor Blue = BrotherPalette.Colors[2];

    private static List<PathNode> Rect(double x0, double y0, double x1, double y1) =>
        new() { new(new Vec2(x0, y0)), new(new Vec2(x1, y0)), new(new Vec2(x1, y1)), new(new Vec2(x0, y1)) };

    private static FillObject Make(FillEffect effect) => new()
    {
        Outline = Rect(0, 0, 30, 30), Color = Red, Color2 = Blue, Effect = effect, Angle = 0, Underlay = false, PullCompensation = 0,
    };

    private static bool Inside(FillObject f, Vec2 p, double tol = 0.15) =>
        GeometryUtil.PointInContours(p, f.Contours()) || f.Contours().Any(c => GeometryUtil.DistanceToPolyline(p, c, true) <= tol);

    // ---------- Degradê ----------

    [Fact]
    public void Gradient_HasTwoColorsAndBlendsFromOneToTheOther()
    {
        var f = Make(FillEffect.Gradient);
        var steps = f.GenerateSteps(null);
        Assert.Equal(2, steps.Count);
        Assert.Equal(Red, steps[0].Color);
        Assert.Equal(Blue, steps[1].Color);

        var first = steps[0].Runs.SelectMany(r => r).ToList();
        var second = steps[1].Runs.SelectMany(r => r).ToList();
        double Share(double y0, double y1) =>
            (double)first.Count(p => p.Y >= y0 && p.Y < y1) /
            Math.Max(1, first.Count(p => p.Y >= y0 && p.Y < y1) + second.Count(p => p.Y >= y0 && p.Y < y1));

        Assert.True(Share(0, 5) > 0.8, $"começo: {Share(0, 5):0.00}");
        Assert.InRange(Share(12, 18), 0.35, 0.65);
        Assert.True(Share(25, 30) < 0.2, $"fim: {Share(25, 30):0.00}");
    }

    [Fact]
    public void Gradient_Reversed_StartsWithTheSecondColor()
    {
        var f = Make(FillEffect.Gradient);
        f.GradientReverse = true;
        var steps = f.GenerateSteps(null);
        Assert.Equal(Blue, steps[0].Color);
        Assert.Equal(Red, steps[1].Color);
        double meanFirst = steps[0].Runs.SelectMany(r => r).Average(p => p.Y);
        double meanSecond = steps[1].Runs.SelectMany(r => r).Average(p => p.Y);
        Assert.True(meanFirst < meanSecond);
    }

    [Fact]
    public void Gradient_KeepsTheDensityOfAPlainFill_AndStaysInsideTheShape()
    {
        var plain = Make(FillEffect.None).GenerateRuns(null).Sum(r => r.Count);
        var g = Make(FillEffect.Gradient);
        var all = g.GenerateRuns(null).SelectMany(r => r).ToList();
        Assert.InRange(all.Count, plain * 0.75, plain * 1.35);
        Assert.All(all, p => Assert.True(Inside(g, p), $"{p} fora"));
    }

    [Fact]
    public void Gradient_InTheSequencer_IsOneColorChange()
    {
        var d = new EmbDesign();
        d.Objects.Add(Make(FillEffect.Gradient));
        var p = d.BuildPattern("t");
        Assert.Equal(1, p.Stitches.Count(s => s.Command == StitchCommand.ColorChange));
        Assert.Equal(new[] { Red, Blue }, p.Threads.ToArray());
    }

    [Fact]
    public void Gradient_FollowsTheFillAngle()
    {
        var f = Make(FillEffect.Gradient);
        f.Angle = 90;      // linhas verticais: o degradê gira junto (começa à direita)
        var steps = f.GenerateSteps(null);
        Assert.True(steps[0].Runs.SelectMany(r => r).Average(p => p.X) > steps[1].Runs.SelectMany(r => r).Average(p => p.X));   // a 90° o degradê corre da direita para a esquerda
    }

    // ---------- Contorno ----------

    [Fact]
    public void Contour_MakesRingsThatFollowTheShapeInwards()
    {
        var f = Make(FillEffect.Contour);
        f.Outline = Rect(0, 0, 30, 20);
        var runs = f.GenerateRuns(null);
        var pts = runs.SelectMany(r => r).ToList();
        Assert.True(pts.Count > 300);
        Assert.All(pts, p => Assert.True(Inside(f, p), $"{p} fora"));

        var outline = f.Contours()[0];
        double deepest = pts.Max(p => GeometryUtil.DistanceToPolyline(p, outline, true));
        Assert.InRange(deepest, 8, 10.2);                      // chega ao miolo (a forma tem 20 mm de altura)
        Assert.True(runs.Count <= 3, $"{runs.Count} trechos: os anéis deveriam formar um espiral contínuo");
    }

    [Fact]
    public void Contour_AroundAHole_StaysOutOfTheHole()
    {
        var f = Make(FillEffect.Contour);
        f.Holes.Add(Rect(10, 10, 20, 20));
        var pts = f.GenerateRuns(null).SelectMany(r => r).ToList();
        Assert.NotEmpty(pts);
        Assert.DoesNotContain(pts, p => p.X > 10.1 && p.X < 19.9 && p.Y > 10.1 && p.Y < 19.9);
        Assert.Contains(pts, p => p.X < 10.4 && p.X > 9.6 && p.Y > 11 && p.Y < 19);    // há pontos rente ao furo
    }

    [Fact]
    public void ContourRings_ShrinkByTheSpacing()
    {
        var rect = new IReadOnlyList<Vec2>[] { new List<Vec2> { new(0, 0), new(20, 0), new(20, 10), new(0, 10) } };
        var wide = ContourFill.Rings(rect, 1.0, 0.5);
        var tight = ContourFill.Rings(rect, 0.5, 0.25);
        Assert.InRange(wide.Count, 4, 6);                      // meia altura (5 mm) / 1 mm
        Assert.InRange(tight.Count, 9, 11);
    }

    // ---------- Padrões ----------

    [Theory]
    [InlineData(FillPattern.Wave)]
    [InlineData(FillPattern.Zigzag)]
    [InlineData(FillPattern.Diamond)]
    [InlineData(FillPattern.Crosshatch)]
    public void Patterns_FillTheShapeAndStayInside(FillPattern pattern)
    {
        var f = Make(FillEffect.Pattern);
        f.Pattern = pattern;
        f.Holes.Add(Rect(10, 10, 20, 20));
        var pts = f.GenerateRuns(null).SelectMany(r => r).ToList();
        Assert.True(pts.Count > 60, $"{pattern}: {pts.Count} pontos");
        Assert.All(pts, p => Assert.True(Inside(f, p, 0.2), $"{pattern}: {p} fora"));
        Assert.DoesNotContain(pts, p => p.X > 10.3 && p.X < 19.7 && p.Y > 10.3 && p.Y < 19.7);
    }

    [Fact]
    public void WavePattern_IsNotAStraightLine()
    {
        var f = Make(FillEffect.Pattern);
        f.Pattern = FillPattern.Wave;
        f.PatternSize = 4;
        var run = f.GenerateRuns(null).OrderByDescending(r => r.Count).First();
        double minY = run.Min(p => p.Y), maxY = run.Max(p => p.Y);
        Assert.True(maxY - minY > 1.0, "a onda precisa subir e descer");
    }

    [Fact]
    public void Crosshatch_HasBothHorizontalAndVerticalStitches()
    {
        var f = Make(FillEffect.Pattern);
        f.Pattern = FillPattern.Crosshatch;
        var segs = f.GenerateRuns(null).SelectMany(r => r.Zip(r.Skip(1))).Where(s => Vec2.Distance(s.First, s.Second) > 0.5).ToList();
        Assert.Contains(segs, s => Math.Abs(s.First.Y - s.Second.Y) < 0.01 && Math.Abs(s.First.X - s.Second.X) > 1);
        Assert.Contains(segs, s => Math.Abs(s.First.X - s.Second.X) < 0.01 && Math.Abs(s.First.Y - s.Second.Y) > 1);
    }

    [Fact]
    public void EffectProperties_SurviveSaveAndLoad()
    {
        var f = Make(FillEffect.Gradient);
        f.GradientReverse = true;
        f.Pattern = FillPattern.Diamond;
        f.PatternSize = 5;
        var d = new EmbDesign();
        d.Objects.Add(f);
        var back = Assert.IsType<FillObject>(Assert.Single(EmbDesign.FromJson(d.ToJson()).Objects));
        Assert.Equal(FillEffect.Gradient, back.Effect);
        Assert.Equal(Blue, back.Color2);
        Assert.True(back.GradientReverse);
        Assert.Equal(FillPattern.Diamond, back.Pattern);
        Assert.Equal(5, back.PatternSize);
    }

    [Fact]
    public void OldProjects_WithoutEffectFields_LoadAsPlainFills()
    {
        var json = """
            {"FormatVersion":1,"HoopName":"Médio 130 × 180 mm","Objects":[{"tipo":"preenchimento","Id":"6f1c6b8e-8f5a-4d6f-9a53-0d6a0f9d1a11","Name":"x","Outline":[{"P":{"X":0,"Y":0},"Curve":false},{"P":{"X":10,"Y":0},"Curve":false},{"P":{"X":10,"Y":10},"Curve":false}]}]}
            """;
        var f = Assert.IsType<FillObject>(Assert.Single(EmbDesign.FromJson(json).Objects));
        Assert.Equal(FillEffect.None, f.Effect);
        Assert.NotEmpty(f.GenerateRuns(null));
    }
}
