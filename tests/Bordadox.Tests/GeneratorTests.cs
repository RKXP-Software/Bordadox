using Bordadox.Core.Design;
using Bordadox.Core.Formats;
using Bordadox.Core.Generators;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.Tests;

public class GeneratorTests
{
    private static List<PathNode> Rect(double x0, double y0, double x1, double y1) => new()
    {
        new(new(x0, y0)), new(new(x1, y0)), new(new(x1, y1)), new(new(x0, y1)),
    };

    private static IEnumerable<(Vec2 A, Vec2 B)> Stitches(IEnumerable<List<Vec2>> runs) =>
        runs.SelectMany(r => r.Zip(r.Skip(1)));

    [Fact]
    public void Flatten_CornersOnly_KeepsNodes()
    {
        var pts = PathUtil.Flatten(Rect(0, 0, 10, 5), closed: true);
        Assert.Equal(4, pts.Count);
    }

    [Fact]
    public void Flatten_CurveNodes_PassesThroughNodesSmoothly()
    {
        var nodes = new List<PathNode> { new(new(0, 0)), new(new(10, 10), true), new(new(20, 0)) };
        var pts = PathUtil.Flatten(nodes, false);
        Assert.True(pts.Count > 10);
        Assert.Contains(pts, p => Vec2.Distance(p, new Vec2(10, 10)) < 1e-9);
        Assert.Equal(new Vec2(20, 0), pts[^1]);
    }

    [Fact]
    public void Run_RespectsStitchLengthAndCorners()
    {
        var nodes = new List<PathNode> { new(new(0, 0)), new(new(10, 0)), new(new(10, 7)) };
        var pts = RunGenerator.Run(nodes, false, 2.5);
        Assert.Contains(new Vec2(10, 0), pts);
        Assert.All(pts.Zip(pts.Skip(1)), s => Assert.InRange(Vec2.Distance(s.First, s.Second), 1.5, 2.5 + 1e-6));
    }

    [Fact]
    public void Fill_Rectangle_CoversAreaInsideBounds()
    {
        var fill = new FillObject { Outline = Rect(0, 0, 20, 10), Angle = 0, Underlay = false, PullCompensation = 0 };
        var runs = fill.GenerateRuns(null);

        Assert.Single(runs); // retângulo = uma região só, sem saltos
        var pts = runs.SelectMany(r => r).ToList();
        Assert.All(pts, p => Assert.True(p.X >= -1e-6 && p.X <= 20 + 1e-6 && p.Y >= 0 && p.Y <= 10));
        int rows = pts.Select(p => Math.Round(p.Y, 3)).Distinct().Count();
        Assert.InRange(rows, 24, 26); // 10 mm / 0,4 mm
        Assert.All(Stitches(runs), s => Assert.True(Vec2.Distance(s.A, s.B) <= 3.5 + 0.5));
    }

    [Fact]
    public void Fill_WithHole_DoesNotStitchAcrossHole()
    {
        var fill = new FillObject
        {
            Outline = Rect(0, 0, 30, 30),
            Holes = { Rect(10, 10, 20, 20) },
            Angle = 0, Underlay = false, PullCompensation = 0,
        };
        var runs = fill.GenerateRuns(null);
        foreach (var (a, b) in Stitches(runs))
        {
            var mid = Vec2.Lerp(a, b, 0.5);
            bool insideHole = mid.X > 10.2 && mid.X < 19.8 && mid.Y > 10.2 && mid.Y < 19.8;
            Assert.False(insideHole, $"ponto atravessa o furo: {a} -> {b}");
        }
    }

    [Fact]
    public void Fill_UShape_SplitsIntoRegionsWithoutLeavingShape()
    {
        // U invertido: duas pernas ligadas por cima.
        var u = new List<PathNode>
        {
            new(new(0, 0)), new(new(30, 0)), new(new(30, 30)), new(new(20, 30)),
            new(new(20, 10)), new(new(10, 10)), new(new(10, 30)), new(new(0, 30)),
        };
        var fill = new FillObject { Outline = u, Angle = 0, Underlay = false, PullCompensation = 0 };
        var runs = fill.GenerateRuns(null);
        Assert.True(runs.Count >= 2);
        var contour = new[] { (IReadOnlyList<Vec2>)u.Select(n => n.P).ToList() };
        foreach (var (a, b) in Stitches(runs))
            Assert.True(GeometryUtil.PointInContours(Vec2.Lerp(a, b, 0.5), contour) ||
                        GeometryUtil.DistanceToPolyline(Vec2.Lerp(a, b, 0.5), contour[0], true) < 0.5);
    }

    [Fact]
    public void Satin_AlternatesSidesWithinColumn()
    {
        var satin = new SatinObject
        {
            Nodes = { new(new(0, 0)), new(new(0, 4)), new(new(20, 0)), new(new(20, 4)) },
            CenterUnderlay = false, PullCompensation = 0,
        };
        var pts = satin.GenerateRuns(null).Single();
        Assert.All(pts, p => Assert.True(Math.Abs(p.Y) < 1e-6 || Math.Abs(p.Y - 4) < 1e-6));
        Assert.All(pts.Zip(pts.Skip(1)), s => Assert.NotEqual(Math.Round(s.First.Y), Math.Round(s.Second.Y)));
        var sideA = pts.Where(p => Math.Abs(p.Y) < 1e-6).ToList();
        Assert.InRange(sideA.Count, 45, 56); // 20 mm / 0,4 mm
    }

    [Fact]
    public void Satin_WideColumnIsSplit()
    {
        var satin = new SatinObject
        {
            Nodes = { new(new(0, 0)), new(new(0, 15)), new(new(10, 0)), new(new(10, 15)) },
            CenterUnderlay = false,
        };
        var runs = satin.GenerateRuns(null);
        Assert.All(Stitches(runs), s => Assert.True(Vec2.Distance(s.A, s.B) <= 7.5));
    }

    [Fact]
    public void Sequencer_ColorChangesAndTrims()
    {
        var design = new EmbDesign();
        design.Objects.Add(new RunObject { Nodes = { new(new(0, 0)), new(new(20, 0)) }, Color = BrotherPalette.Colors[5] });
        design.Objects.Add(new RunObject { Nodes = { new(new(0, 30)), new(new(20, 30)) }, Color = BrotherPalette.Colors[5] });
        design.Objects.Add(new RunObject { Nodes = { new(new(0, 60)), new(new(20, 60)) }, Color = BrotherPalette.Colors[2] });
        var p = design.BuildPattern();

        Assert.Equal(2, p.Threads.Count);
        Assert.Single(p.Stitches, s => s.Command == StitchCommand.ColorChange);
        Assert.Equal(3, p.Stitches.Count(s => s.Command == StitchCommand.Trim)); // salto longo + troca + fim
        Assert.Equal(StitchCommand.End, p.Stitches[^1].Command);
    }

    [Fact]
    public void Design_JsonRoundTrip_ProducesSameStitches()
    {
        var design = new EmbDesign();
        design.Objects.Add(new FillObject { Outline = Rect(0, 0, 30, 20), Holes = { Rect(5, 5, 10, 10) }, FillType = FillType.Satin });
        design.Objects.Add(new SatinObject { Nodes = { new(new(0, 30)), new(new(0, 34)), new(new(15, 30), true), new(new(15, 35), true), new(new(30, 30)), new(new(30, 34)) } });
        design.Objects.Add(new RunObject { Nodes = { new(new(0, 40)), new(new(10, 45), true), new(new(20, 40)) }, Triple = true, Color = BrotherPalette.Colors[13] });

        var json = design.ToJson();
        var back = EmbDesign.FromJson(json);
        Assert.Equal(design.BuildPattern().Stitches, back.BuildPattern().Stitches);
        Assert.Contains("\"tipo\": \"preenchimento\"", json);
    }

    [Fact]
    public void Design_FromPattern_KeepsAllStitches()
    {
        var path = Path.Combine(SampleFileTests.SamplesDir, "teste_nq1400e.pes");
        if (!File.Exists(path)) return;
        var original = EmbroideryFormats.ReadFile(path);
        var design = EmbDesign.FromPattern(original);
        Assert.Equal(3, design.Objects.Count);
        var rebuilt = design.BuildPattern();
        Assert.Equal(3, rebuilt.Threads.Count);
        Assert.InRange(rebuilt.StitchCount, original.StitchCount * 0.95, original.StitchCount * 1.1);
    }
}
