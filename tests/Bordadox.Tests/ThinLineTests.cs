using Bordadox.Core.Design;
using Bordadox.Core.Digitize;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.Tests;

public class SkeletonTests
{
    private static bool[] Draw(int w, int h, Func<int, int, bool> f)
    {
        var m = new bool[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) m[y * w + x] = f(x, y);
        return m;
    }

    [Fact]
    public void Bar_BecomesOneLineWithTwoEnds()
    {
        var mask = Draw(80, 30, (x, y) => x >= 10 && x < 70 && y >= 12 && y < 17);   // 60 × 5
        var skel = Skeleton.Thin(mask, 80, 30);
        Assert.InRange(skel.Count(s => s), 48, 62);          // linha de ~1 pixel de espessura e ~60 de comprimento
        var paths = Skeleton.Trace(skel, 80, 30);
        var path = Assert.Single(paths);
        Assert.InRange(path.Count, 48, 62);
        Assert.All(path, p => Assert.InRange(p.Y, 13, 15)); // no meio da barra
    }

    [Fact]
    public void Plus_HasAJunctionAndFourArms()
    {
        var mask = Draw(80, 80, (x, y) => (Math.Abs(x - 40) <= 2 && y >= 10 && y < 70) || (Math.Abs(y - 40) <= 2 && x >= 10 && x < 70));
        var skel = Skeleton.Thin(mask, 80, 80);
        var paths = Skeleton.Trace(skel, 80, 80);
        Assert.InRange(paths.Count, 4, 6);
        Assert.All(paths.Where(p => p.Count > 10), p => Assert.InRange(p.Count, 20, 40)); // cada braço ~28 px
    }

    [Fact]
    public void Ring_IsOneClosedPath()
    {
        var mask = Draw(100, 100, (x, y) => { double d = Math.Sqrt(Math.Pow(x - 50, 2) + Math.Pow(y - 50, 2)); return d >= 30 && d <= 35; });
        var skel = Skeleton.Thin(mask, 100, 100);
        // Um caminho principal cobre quase todo o anel; sobram só pixels soltos de quina (1 pixel), que são descartados depois.
        var paths = Skeleton.Trace(skel, 100, 100);
        var path = paths.OrderByDescending(p => p.Count).First();
        Assert.True(path.Count >= skel.Count(s => s) * 0.8, $"caminho principal com {path.Count} de {skel.Count(s => s)} pixels");
        Assert.All(paths.Where(p => p != path), p => Assert.True(p.Count <= 3));
        Assert.Equal(path[0], path[^1]);                       // fechou
        Assert.All(path, p => Assert.InRange(Math.Sqrt(Math.Pow(p.X - 50, 2) + Math.Pow(p.Y - 50, 2)), 31.2, 34.8));
        Assert.InRange(path.Count, 180, 240);                  // ≈ 2π · 32,5
    }

    [Fact]
    public void ThinPart_TakesTheStrokeButKeepsTheThickShapeWithItsCorners()
    {
        // Quadrado grosso 40 × 40 com um traço de 4 px saindo dele.
        var mask = Draw(160, 80, (x, y) => (x >= 10 && x < 50 && y >= 20 && y < 60) || (x >= 50 && x < 140 && y >= 38 && y < 42));
        var thin = Skeleton.ThinPart(mask, 160, 80, radius: 3);

        Assert.True(thin[40 * 160 + 100]);          // meio do traço é fino
        Assert.False(thin[40 * 160 + 30]);          // centro do quadrado é grosso
        Assert.False(thin[21 * 160 + 11]);          // canto do quadrado continua no preenchimento (não arredondado)
        Assert.False(thin[58 * 160 + 48]);
        int thinCount = thin.Count(t => t);
        Assert.InRange(thinCount, 90 * 4 - 40, 90 * 4 + 40);   // o traço: ~90 × 4 pixels
    }

    [Fact]
    public void ThinPart_OfOnlyThickShapes_IsEmpty()
    {
        var mask = Draw(100, 100, (x, y) => x >= 20 && x < 80 && y >= 20 && y < 80);
        Assert.DoesNotContain(true, Skeleton.ThinPart(mask, 100, 100, 3));
    }
}

public class ThinLineDigitizeTests
{
    private static DigitizeResult Run(RgbaImage img, Action<DigitizeOptions>? tweak = null)
    {
        var o = new DigitizeOptions { WidthMm = 100, CropToContent = false, ThinLines = true };
        tweak?.Invoke(o);
        return AutoDigitizer.Run(img, o);
    }

    [Fact]
    public void ThinRing_BecomesAClosedRunningStitchAlongItsCenter()
    {
        var img = Synth.Blank(400, 300);
        Synth.Paint(img, Synth.Ring(200, 150, 59, 61), 20, 20, 20);    // fio de 2 px = 0,5 mm, raio 60 px = 15 mm

        var r = Run(img);
        var run = Assert.IsType<RunObject>(Assert.Single(r.Objects));
        Assert.True(run.Closed);
        Assert.True(run.Triple);
        Assert.Equal(1, r.ThinLines);
        Assert.All(run.Nodes, n => Assert.InRange(Vec2.Distance(n.P, Vec2.Zero), 15 - 0.4, 15 + 0.4));
        Assert.InRange(run.Nodes.Count, 6, 120);

        var design = new EmbDesign();
        design.Objects.Add(run);
        var p = design.BuildPattern();
        Assert.All(p.Stitches.Where(s => s.Command == StitchCommand.Stitch),
            s => Assert.InRange(Math.Sqrt(s.X * s.X + s.Y * s.Y) / 10.0, 15 - 0.6, 15 + 0.6));
        Assert.InRange(p.StitchCount, 100, 600);
    }

    [Fact]
    public void ThickShapeBesideAThinLine_GivesOneFillAndOneLine()
    {
        var img = Synth.Blank(400, 300);
        Synth.Paint(img, Synth.Rect(30, 80, 150, 220), 30, 90, 160);                 // quadrado grosso (30 × 35 mm)
        Synth.Paint(img, Synth.Rect(150, 148, 370, 151), 30, 90, 160);               // fio de 3 px (0,75 mm) saindo dele

        var r = Run(img);
        Assert.Single(r.Objects.OfType<FillObject>());
        var line = Assert.Single(r.Objects.OfType<RunObject>());
        Assert.False(line.Closed);
        // O fio vai de x ≈ 150 px até 370 px (≈ −12,5 a 42,5 mm) na altura do centro.
        var b = line.Bounds();
        Assert.InRange(b.MinX, -14, -8);
        Assert.InRange(b.MaxX, 40, 43.5);
        Assert.InRange(b.Height, 0, 1.0);
        // O quadrado conserva os cantos: continua com área ≈ 30 × 35 mm.
        var fill = r.Objects.OfType<FillObject>().Single();
        Assert.InRange(fill.Bounds().Width, 29, 31.5);
        Assert.InRange(fill.Bounds().Height, 34, 36.5);
    }

    [Fact]
    public void WithThinLinesOff_TheStrokeIsDropped()
    {
        var img = Synth.Blank(400, 300);
        Synth.Paint(img, Synth.Rect(30, 148, 370, 151), 30, 90, 160);
        var off = Run(img, o => o.ThinLines = false);
        Assert.Empty(off.Objects.OfType<RunObject>());
        var on = Run(img);
        Assert.Single(on.Objects.OfType<RunObject>());
    }

    [Fact]
    public void ShortSpurs_AndSpecksAreNotTurnedIntoLines()
    {
        var img = Synth.Blank(400, 300);
        Synth.Paint(img, Synth.Rect(40, 100, 360, 104), 10, 10, 10);   // fio principal
        Synth.Paint(img, Synth.Rect(200, 98, 202, 100), 10, 10, 10);   // saliência de 0,5 mm no fio (vira espinho do esqueleto)
        Synth.Paint(img, Synth.Rect(300, 200, 306, 203), 10, 10, 10);  // mancha pequena (1,5 × 0,75 mm)
        var r = Run(img);
        var lines = r.Objects.OfType<RunObject>().ToList();
        // A saliência engrossa o fio naquele ponto: o fio pode virar duas linhas com um pequeno preenchimento no meio
        // (o traço continua, só muda de tipo por um trecho). O que não pode: linhas extras vindas da mancha ou do espinho.
        Assert.InRange(lines.Count, 1, 2);
        Assert.All(lines, l => Assert.InRange(l.Bounds().MinY, -12.5, -11.5));   // todas sobre o fio principal (y ≈ −12 mm)
        Assert.InRange(lines.Sum(l => l.Bounds().Width), 70, 82);                // juntas, cobrem quase o fio todo
        Assert.InRange(r.Objects.OfType<FillObject>().Count(), 0, 1);
        Assert.All(r.Objects.OfType<FillObject>(), f => Assert.InRange(f.Bounds().Width, 0, 5)); // o remendo é pequeno
    }

    [Fact]
    public void LinesAndFills_AreOrderedByDistanceWithinAColor()
    {
        var img = Synth.Blank(500, 300);
        Synth.Paint(img, Synth.Rect(20, 100, 120, 200), 10, 120, 10);     // quadrado à esquerda
        Synth.Paint(img, Synth.Rect(120, 148, 480, 151), 10, 120, 10);    // fio ligado a ele, à direita
        var r = Run(img);
        Assert.Equal(2, r.Objects.Count);
        Assert.Single(r.Layers);
        Assert.Equal(2, r.Layers[0].ObjectCount);
    }
}
