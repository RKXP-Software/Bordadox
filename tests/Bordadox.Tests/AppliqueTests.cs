using Bordadox.Core.Design;
using Bordadox.Core.Formats;
using Bordadox.Core.Generators;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.Tests;

public class AppliqueTests
{
    private static readonly ThreadColor Red = BrotherPalette.Colors[5];
    private static readonly ThreadColor Gray = BrotherPalette.Colors[39];

    private static List<PathNode> Square(double x0, double y0, double x1, double y1) =>
        new() { new(new Vec2(x0, y0)), new(new Vec2(x1, y0)), new(new Vec2(x1, y1)), new(new Vec2(x0, y1)) };

    private static AppliqueObject Make() => new() { Outline = Square(-15, -15, 15, 15), Color = Red, GuideColor = Gray, CoverWidth = 3 };

    [Fact]
    public void HasThreeSteps_WithStopsBeforeTheTackDownAndTheCover()
    {
        var steps = Make().GenerateSteps(null);
        Assert.Equal(3, steps.Count);
        Assert.Equal(Gray, steps[0].Color);
        Assert.False(steps[0].StopBefore);          // a guia é a primeira coisa costurada
        Assert.Equal(Gray, steps[1].Color);
        Assert.True(steps[1].StopBefore);           // pausa para colocar o tecido
        Assert.Equal(Red, steps[2].Color);
        Assert.True(steps[2].StopBefore);           // pausa para aparar o tecido
        Assert.All(steps, s => Assert.NotEmpty(s.Runs));
    }

    [Fact]
    public void PlacementLine_CanBeSkipped()
    {
        var a = Make();
        a.PlacementLine = false;
        var steps = a.GenerateSteps(null);
        Assert.Equal(2, steps.Count);
        Assert.True(steps[0].StopBefore);
    }

    [Fact]
    public void CoverSatin_StaysInABandAroundTheOutline()
    {
        var a = Make();
        var cover = a.GenerateSteps(null)[2].Runs.SelectMany(r => r).ToList();
        Assert.True(cover.Count > 200);
        var outline = a.Contours()[0];
        double half = a.CoverWidth / 2 + a.PullCompensation + 0.05;
        foreach (var p in cover)
        {
            double d = GeometryUtil.DistanceToPolyline(p, outline, true);
            Assert.InRange(d, 0, half + 0.5);   // 0,5 de folga: o vértice do leque nas quinas
        }
        // Os pontos tocam as duas bordas da faixa (dentro e fora do contorno).
        Assert.Contains(cover, p => GeometryUtil.PointInContours(p, new[] { outline }));
        Assert.Contains(cover, p => !GeometryUtil.PointInContours(p, new[] { outline }));
    }

    [Fact]
    public void Holes_GetTheirOwnPlacementLineAndCover()
    {
        var a = Make();
        a.Holes.Add(Square(-5, -5, 5, 5));
        var steps = a.GenerateSteps(null);
        Assert.Equal(2, steps[0].Runs.Count);
        Assert.Equal(2, steps[1].Runs.Count);
        Assert.Equal(2, steps[2].Runs.Count);
    }

    [Fact]
    public void SquareCorners_BecomeFans()
    {
        var square = new List<Vec2> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
        var pairs = OutlineSatin.Pairs(square, 3, 0.2);
        var centers = pairs.Select(p => Vec2.Lerp(p.A, p.B, 0.5)).ToList();
        // Num vértice vários pares compartilham o mesmo centro.
        int atCorner = centers.Count(c => Vec2.Distance(c, new Vec2(10, 0)) < 1e-6);
        Assert.InRange(atCorner, 4, 12);
        Assert.All(pairs, p => Assert.InRange(Vec2.Distance(p.A, p.B), 2.9, 4.4));   // a largura se mantém (com folga na quina)
    }

    [Fact]
    public void Sequencer_AddsTwoStops_AndThreeThreadEntries()
    {
        var d = new EmbDesign();
        d.Objects.Add(Make());
        var p = d.BuildPattern("t");
        Assert.Equal(2, p.Stitches.Count(s => s.Command == StitchCommand.ColorChange));
        Assert.Equal(3, p.Threads.Count);
        Assert.Equal(new[] { Gray, Gray, Red }, p.Threads.ToArray());
    }

    [Fact]
    public void Sequencer_StopsEvenWhenTheCoverColorEqualsTheGuideColor()
    {
        var a = Make();
        a.Color = Gray;
        var d = new EmbDesign();
        d.Objects.Add(a);
        var p = d.BuildPattern("t");
        Assert.Equal(2, p.Stitches.Count(s => s.Command == StitchCommand.ColorChange));
    }

    [Fact]
    public void FollowingObjects_StillChangeColorNormally()
    {
        var d = new EmbDesign();
        d.Objects.Add(Make());
        d.Objects.Add(new RunObject { Nodes = new() { new(new Vec2(-10, 20)), new(new Vec2(10, 20)) }, Color = BrotherPalette.Colors[20] });
        var p = d.BuildPattern("t");
        Assert.Equal(3, p.Stitches.Count(s => s.Command == StitchCommand.ColorChange));
        Assert.Equal(4, p.Threads.Count);
    }

    [Fact]
    public void SurvivesSaveAndLoad_AndAPesRoundTrip()
    {
        var d = new EmbDesign();
        d.Objects.Add(Make());
        var back = EmbDesign.FromJson(d.ToJson());
        var a = Assert.IsType<AppliqueObject>(Assert.Single(back.Objects));
        Assert.Equal(Gray, a.GuideColor);
        Assert.Equal(Red, a.Color);
        Assert.Equal(3, a.CoverWidth);

        var p = back.BuildMachinePattern("t");
        using var ms = new MemoryStream();
        new PesFormat().Write(p, ms);
        ms.Position = 0;
        var read = new PesFormat().Read(ms);
        Assert.Equal(p.StitchCount, read.StitchCount);
        Assert.Equal(3, read.Threads.Count);
    }

    [Fact]
    public void MovingTheApplique_MovesAllSteps()
    {
        var a = Make();
        var before = a.GenerateSteps(null).SelectMany(s => s.Runs).SelectMany(r => r).ToList();
        a.Transform(p => new Vec2(p.X + 20, p.Y - 7));
        var after = a.GenerateSteps(null).SelectMany(s => s.Runs).SelectMany(r => r).ToList();
        Assert.Equal(before.Count, after.Count);
        Assert.Equal(before[0].X + 20, after[0].X, 6);
        Assert.Equal(before[0].Y - 7, after[0].Y, 6);
    }

    [Fact]
    public void FillObject_StillBehavesAsBefore_AfterTheRefactor()
    {
        var f = new FillObject { Outline = Square(0, 0, 20, 10), Holes = { Square(5, 3, 15, 7) }, Color = Red };
        Assert.Equal(2, f.Contours().Count);
        Assert.NotEmpty(f.GenerateRuns(null));
        Assert.True(f.HitTest(new Vec2(2, 5), 0.1));
        Assert.False(f.HitTest(new Vec2(10, 5), 0.1));       // dentro do furo
        Assert.Single(new EmbDesign { Objects = { f } }.BuildPattern("t").Threads);
    }
}
