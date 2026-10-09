using Bordadox.Core.Design;
using Bordadox.Core.Generators;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;
using Bordadox.Core.Text;

namespace Bordadox.Tests;

internal static class Shapes
{
    public static List<Vec2> Rect(double x0, double y0, double x1, double y1) => new() { new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1) };

    /// <summary>Barra horizontal de comprimento <paramref name="len"/> e espessura <paramref name="thick"/>, canto superior esquerdo em (x, y).</summary>
    public static List<Vec2> Bar(double x, double y, double len, double thick) => Rect(x, y, x + len, y + thick);

    /// <summary>Contorno único de uma figura formada por retângulos que se tocam (união por rasterização não é necessária: só L e +).</summary>
    public static List<Vec2> L(double len, double thick) => new()
    {
        new(0, 0), new(thick, 0), new(thick, len - thick), new(len, len - thick), new(len, len), new(0, len),
    };

    public static List<Vec2> Plus(double len, double thick)
    {
        double a = (len - thick) / 2, b = a + thick;
        return new() { new(a, 0), new(b, 0), new(b, a), new(len, a), new(len, b), new(b, b), new(b, len), new(a, len), new(a, b), new(0, b), new(0, a), new(a, a) };
    }
}

public class GlyphComponentsTests
{
    [Fact]
    public void RectWithHole_IsOneComponent()
    {
        var parts = GlyphComponents.Split(new[] { Shapes.Rect(0, 0, 10, 10), Shapes.Rect(3, 3, 7, 7) });
        var part = Assert.Single(parts);
        Assert.Equal(2, part.Count);
    }

    [Fact]
    public void SeparateShapes_AreSeparateComponents()
    {
        var parts = GlyphComponents.Split(new[] { Shapes.Rect(0, 0, 4, 10), Shapes.Rect(0, -6, 4, -3) });   // como o "i"
        Assert.Equal(2, parts.Count);
        Assert.All(parts, p => Assert.Single(p));
    }

    [Fact]
    public void IslandInsideAHole_IsItsOwnComponent()
    {
        var parts = GlyphComponents.Split(new[] { Shapes.Rect(0, 0, 20, 20), Shapes.Rect(4, 4, 16, 16), Shapes.Rect(8, 8, 12, 12) });
        Assert.Equal(2, parts.Count);
        Assert.Contains(parts, p => p.Count == 2);   // anel: contorno externo + furo
        Assert.Contains(parts, p => p.Count == 1);   // a ilha
    }
}

public class ColumnSatinTests
{
    private static IReadOnlyList<IReadOnlyList<Vec2>> One(List<Vec2> c) => new IReadOnlyList<Vec2>[] { c };

    [Fact]
    public void StraightBar_IsOneColumnWithStitchesAcrossIt()
    {
        var cols = ColumnSatin.Build(One(Shapes.Bar(0, 0, 20, 3)), 0.2, 6);
        var col = Assert.Single(cols!);
        Assert.InRange(col.Count, 80, 130);
        Assert.All(col, p =>
        {
            Assert.InRange(Math.Abs(p.A.Y - p.B.Y), 2.6, 3.2);    // atravessa a espessura
            Assert.InRange(Math.Abs(p.A.X - p.B.X), 0, 0.25);     // e é perpendicular à barra
            Assert.InRange(p.A.X, -0.1, 20.1);
            Assert.InRange(p.B.X, -0.1, 20.1);
        });
        // A coluna vai de uma ponta à outra (a ponta do esqueleto é prolongada até a borda).
        double min = col.Min(p => p.A.X), max = col.Max(p => p.A.X);
        Assert.InRange(min, 0, 1.2);
        Assert.InRange(max, 18.8, 20);
    }

    [Fact]
    public void VeryThickShape_IsLeftToTheNormalFill()
    {
        Assert.Null(ColumnSatin.Build(One(Shapes.Rect(0, 0, 20, 20)), 0.2, 6));
    }

    [Fact]
    public void LShape_IsSplitAtTheCornerIntoTwoColumns()
    {
        var cols = ColumnSatin.Build(One(Shapes.L(20, 3)), 0.2, 6)!;
        Assert.Equal(2, cols.Count);
        // Uma coluna é vertical (pontos horizontais) e a outra horizontal (pontos verticais).
        int horizontalStitches = cols.Count(c => c.Average(p => Math.Abs(p.A.X - p.B.X)) > 2);
        int verticalStitches = cols.Count(c => c.Average(p => Math.Abs(p.A.Y - p.B.Y)) > 2);
        Assert.Equal(1, horizontalStitches);
        Assert.Equal(1, verticalStitches);
    }

    [Fact]
    public void Plus_IsTwoLongColumnsThatCross()
    {
        var cols = ColumnSatin.Build(One(Shapes.Plus(24, 3)), 0.2, 6)!;
        Assert.Equal(2, cols.Count);
        Assert.All(cols, c => Assert.InRange(c.Count, 100, 140));   // cada uma atravessa o desenho (~24 mm / 0,2)
    }

    [Fact]
    public void Ring_IsAClosedColumn()
    {
        var cols = ColumnSatin.Build(new[] { Shapes.Rect(0, 0, 20, 20), Shapes.Rect(3, 3, 17, 17) }, 0.2, 6);
        Assert.NotNull(cols);
        Assert.Single(cols!);
    }

    [Fact]
    public void ColumnStitches_StayInsideTheShape()
    {
        var shape = Shapes.L(20, 3);
        var cols = ColumnSatin.Build(One(shape), 0.2, 6)!;
        var contours = new IReadOnlyList<Vec2>[] { shape };
        foreach (var p in cols.SelectMany(c => c).SelectMany(p => new[] { p.A, p.B }))
            Assert.True(GeometryUtil.PointInContours(p, contours) || GeometryUtil.DistanceToPolyline(p, shape, true) < 0.1, $"{p} fora da forma");
    }
}

public class InsideRouterTests
{
    private static readonly IReadOnlyList<IReadOnlyList<Vec2>> Frame = new IReadOnlyList<Vec2>[] { Shapes.Rect(0, 0, 30, 30), Shapes.Rect(10, 10, 20, 20) };

    private static bool Inside(Vec2 p) => GeometryUtil.PointInContours(p, Frame);

    [Fact]
    public void RoutesAroundAHole_StayingInside()
    {
        var router = new InsideRouter(Frame);
        var way = router.Route(new Vec2(2, 15), new Vec2(28, 15), 60);
        Assert.NotNull(way);
        Assert.NotEmpty(way!);                         // a reta atravessaria o furo: tem desvio
        var pts = new List<Vec2> { new(2, 15) };
        pts.AddRange(way);
        pts.Add(new Vec2(28, 15));
        for (int i = 1; i < pts.Count; i++)
            for (int k = 0; k <= 20; k++)
                Assert.True(Inside(Vec2.Lerp(pts[i - 1], pts[i], k / 20.0)), $"caminho sai da forma entre {pts[i - 1]} e {pts[i]}");
        Assert.InRange(GeometryUtil.PolylineLength(pts), 26, 45);
    }

    [Fact]
    public void WithoutAHole_NoDetourIsNeeded()
    {
        var router = new InsideRouter(new IReadOnlyList<Vec2>[] { Shapes.Rect(0, 0, 30, 30) });
        var way = router.Route(new Vec2(2, 15), new Vec2(28, 15), 40);
        Assert.NotNull(way);
        Assert.Empty(way!);
    }

    [Fact]
    public void ReturnsNull_WhenTheDetourIsTooLong()
    {
        var router = new InsideRouter(Frame);
        Assert.Null(router.Route(new Vec2(2, 15), new Vec2(28, 15), 27));
    }

    [Fact]
    public void ConnectInside_JoinsRunsAcrossAHole_WithoutLeavingTheShape()
    {
        var runs = new List<List<Vec2>>
        {
            new() { new(2, 12), new(3, 15) },
            new() { new(27, 15), new(28, 18) },
        };
        var joined = FillGenerator.ConnectInside(runs, Frame, maxConnectMm: 20);
        var run = Assert.Single(joined);
        Assert.All(run, p => Assert.True(Inside(p) || GeometryUtil.DistanceToPolyline(p, Frame[0], true) < 0.5, $"{p} fora"));
        Assert.True(run.Count > 4);
    }

    [Fact]
    public void ConnectInside_StillSeparatesRunsWhenTheDetourIsLong()
    {
        var runs = new List<List<Vec2>>
        {
            new() { new(2, 12), new(3, 15) },
            new() { new(27, 15), new(28, 18) },
        };
        Assert.Equal(2, FillGenerator.ConnectInside(runs, Frame, maxConnectMm: 8).Count);
    }
}

public class LetterQualityTests
{
    public LetterQualityTests() => GlyphService.Provider = new FakeGlyphProvider();

    private static TextObject Make(string text, double height = 10) =>
        new() { Text = text, HeightMm = height, Underlay = false, PullCompensation = 0, FillType = FillType.Satin };

    [Fact]
    public void Kerning_PullsAVTogether_AndCanBeTurnedOff()
    {
        double em = 10 / FakeGlyphProvider.Cap;
        double kerned = Make("AV").Bounds().Width;
        var off = Make("AV");
        off.Kerning = false;
        Assert.Equal(off.Bounds().Width - 0.1 * em, kerned, 3);

        // Pares sem kerning não mudam.
        Assert.Equal(Make("AB").Bounds().Width, Make("AB").Bounds().Width);
        var ab = Make("AB");
        ab.Kerning = false;
        Assert.Equal(ab.Bounds().Width, Make("AB").Bounds().Width, 3);
    }

    [Fact]
    public void SatinColumns_StaysInsideTheLetters_AndNotInTheHoleOfTheO()
    {
        var t = Make("O", 14);
        t.SatinColumns = true;
        var runs = t.GenerateRuns(null);
        Assert.NotEmpty(runs);
        var box = t.Bounds().Inflate(0.3);
        Assert.All(runs.SelectMany(r => r), p => Assert.True(box.Contains(p), $"{p} fora da letra"));

        // O miolo do "O" (menos uma folga) fica sem pontos.
        double em = 14 / FakeGlyphProvider.Cap;
        double x0 = 0.15 * em + 0.4, x1 = (FakeGlyphProvider.Width - 0.15) * em - 0.4;
        double y0 = (-FakeGlyphProvider.Cap + 0.15) * em + 0.4, y1 = -0.15 * em - 0.4;
        Assert.DoesNotContain(runs.SelectMany(r => r), p => p.X > x0 && p.X < x1 && p.Y > y0 && p.Y < y1);
    }

    [Fact]
    public void SatinColumns_CanBeTurnedOff_ForTheOldBehaviour()
    {
        var on = Make("HIO", 10);
        var off = Make("HIO", 10);
        off.SatinColumns = false;
        Assert.NotEmpty(on.GenerateRuns(null));
        Assert.NotEmpty(off.GenerateRuns(null));
        Assert.NotEqual(on.GenerateRuns(null).Sum(r => r.Count), off.GenerateRuns(null).Sum(r => r.Count));
    }

    [Fact]
    public void MovingTheText_MovesTheColumns_WithoutRebuildingThem()
    {
        var t = Make("HO", 12);
        var before = t.GenerateRuns(null).SelectMany(r => r).ToList();
        t.Origin = new Vec2(30, -5);
        var after = t.GenerateRuns(null).SelectMany(r => r).ToList();
        Assert.Equal(before.Count, after.Count);
        for (int i = 0; i < before.Count; i += 7)
        {
            Assert.Equal(before[i].X + 30, after[i].X, 6);
            Assert.Equal(before[i].Y - 5, after[i].Y, 6);
        }
    }

    [Fact]
    public void RotatedText_KeepsAllStitchesInsideTheRotatedBounds()
    {
        var t = Make("HO", 12);
        t.RotationDeg = 30;
        var box = t.Bounds().Inflate(0.4);
        Assert.All(t.GenerateRuns(null).SelectMany(r => r), p => Assert.True(box.Contains(p)));
    }

    [Fact]
    public void WholeDesign_WithColumnSatinText_BuildsAValidPattern()
    {
        var d = new EmbDesign();
        d.Objects.Add(new TextObject { Text = "HOLA", HeightMm = 10, Origin = new Vec2(-15, 5), FillType = FillType.Satin, Color = BrotherPalette.Colors[5] });
        var p = d.BuildPattern("t");
        Assert.InRange(p.StitchCount, 200, 20000);
        Assert.All(p.Stitches.Where(s => s.Command == StitchCommand.Stitch), s => Assert.InRange(Math.Abs(s.X), 0, 400));
    }
}
