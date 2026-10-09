using Bordadox.Core.Design;
using Bordadox.Core.Digitize;
using Bordadox.Core.Stitches;

namespace Bordadox.Tests;

public class ColorMatchingTests
{
    [Theory]
    [InlineData(50, 2.6772, -79.7751, 50, 0, -82.7485, 2.0425)]   // pares de referência de Sharma et al. (2005)
    [InlineData(50, 2.5, 0, 50, 0, -2.5, 4.3065)]
    [InlineData(50, 2.5, 0, 73, 25, -18, 27.1492)]
    [InlineData(50, 2.5, 0, 50, 2.5, 0, 0)]
    public void DeltaE2000_MatchesPublishedValues(float l1, float a1, float b1, float l2, float a2, float b2, double expected)
    {
        var d = DeltaE2000.Between(new Lab(l1, a1, b1), new Lab(l2, a2, b2));
        Assert.Equal(expected, d, 3);
    }

    [Fact]
    public void EveryPaletteColor_MapsToItself()
    {
        for (int i = 1; i < BrotherPalette.Colors.Count; i++)
        {
            var c = BrotherPalette.Colors[i];
            var found = BrotherPalette.Colors[BrotherPalette.NearestIndex(c)];
            Assert.Equal((c.R, c.G, c.B), (found.R, found.G, found.B));
        }
    }

    [Fact]
    public void MustardGold_MapsToAGoldThread_NotToBrown()
    {
        // O dourado do logo de teste (190,143,45) caía em "Marrom claro" com a distância em RGB.
        var t = BrotherPalette.Colors[BrotherPalette.NearestIndex(new ThreadColor(190, 143, 45, "", ""))];
        Assert.Contains(t.Name, new[] { "Latão", "Dourado escuro", "Ouro colheita" });
    }
}

public class ShadeMergerTests
{
    private static QuantizedImage Quantize(RgbaImage img) => Quantizer.Run(img, new QuantizeOptions { MaxColors = 8 });

    private static void GoldGradient(RgbaImage img, int x0, int x1, int y0, int y1)
    {
        for (int x = x0; x < x1; x++)
        {
            double t = (x - x0) / (double)(x1 - x0);
            byte r = (byte)(250 + (160 - 250) * t), g = (byte)(225 + (115 - 225) * t), b = (byte)(130 + (30 - 130) * t);
            for (int y = y0; y < y1; y++) img.Set(x, y, r, g, b);
        }
    }

    [Fact]
    public void SmoothGoldGradient_BecomesOneColor()
    {
        var img = Synth.Blank(400, 200);
        GoldGradient(img, 40, 360, 50, 150);
        var q = Quantize(img);
        Assert.True(q.Colors.Count >= 2, $"o quantizador deveria dividir o degradê (deu {q.Colors.Count})");

        var merged = ShadeMerger.Merge(q, img);
        Assert.Single(merged.Colors);
        Assert.Equal(q.Labels.Count(l => l >= 0), merged.Labels.Count(l => l == 0));
    }

    [Fact]
    public void TwoFlatTonesOfTheSameHue_StayApart()
    {
        var img = Synth.Blank(400, 200);
        Synth.Paint(img, Synth.Rect(20, 40, 200, 160), 200, 30, 30);     // vermelho
        Synth.Paint(img, Synth.Rect(200, 40, 380, 160), 240, 140, 140);  // rosa colado nele: borda seca
        var q = Quantize(img);
        Assert.Equal(2, q.Colors.Count);
        Assert.Equal(2, ShadeMerger.Merge(q, img).Colors.Count);
    }

    [Fact]
    public void DifferentHues_NeverMerge_EvenWithASoftTransition()
    {
        var img = Synth.Blank(400, 200);
        for (int x = 40; x < 360; x++)
        {
            double t = (x - 40) / 320.0;     // azul → verde, suave
            for (int y = 50; y < 150; y++) img.Set(x, y, (byte)(30 + 20 * t), (byte)(60 + 140 * t), (byte)(220 - 150 * t));
        }
        var q = Quantize(img);
        Assert.True(ShadeMerger.Merge(q, img).Colors.Count >= 2);
    }

    [Fact]
    public void Digitize_GoldGradientOnBlack_GivesOneThread()
    {
        var img = Synth.Blank(400, 300, 0, 0, 0);
        GoldGradient(img, 40, 360, 80, 220);
        var result = AutoDigitizer.Run(img, new DigitizeOptions { WidthMm = 100, CropToContent = false });
        Assert.Single(result.Layers);
    }
}

public class JunctionJoinTests
{
    private static bool[] Draw(int w, int h, Func<int, int, bool> f)
    {
        var m = new bool[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) m[y * w + x] = f(x, y);
        return m;
    }

    private static List<List<PointI>> Skel(bool[] mask, int w, int h, out bool[] skeleton)
    {
        skeleton = Skeleton.Thin(mask, w, h);
        Skeleton.Prune(skeleton, w, h, 6);
        return Skeleton.Trace(skeleton, w, h);
    }

    [Fact]
    public void Crossing_BecomesTwoLongLines()
    {
        var mask = Draw(120, 120, (x, y) => (Math.Abs(x - 60) <= 2 && y >= 10 && y < 110) || (Math.Abs(y - 60) <= 2 && x >= 10 && x < 110));
        var paths = Skel(mask, 120, 120, out var skel);
        Assert.True(paths.Count(p => p.Count > 10) >= 4, "sem juntar, são 4 braços");

        var joined = Skeleton.JoinAtJunctions(paths, skel, 120, 120).Where(p => p.Count > 10).ToList();
        Assert.Equal(2, joined.Count);
        Assert.All(joined, p => Assert.InRange(p.Count, 90, 105));   // cada uma atravessa o desenho (~100 px)
        Assert.Equal(1, joined.Count(p => Math.Abs(p[0].X - p[^1].X) > 80));   // uma horizontal
        Assert.Equal(1, joined.Count(p => Math.Abs(p[0].Y - p[^1].Y) > 80));   // e uma vertical
    }

    [Fact]
    public void TJunction_KeepsTheBarWholeAndTheStemApart()
    {
        var mask = Draw(140, 100, (x, y) => (Math.Abs(y - 20) <= 2 && x >= 10 && x < 130) || (Math.Abs(x - 70) <= 2 && y >= 20 && y < 90));
        var paths = Skel(mask, 140, 100, out var skel);
        var joined = Skeleton.JoinAtJunctions(paths, skel, 140, 100).Where(p => p.Count > 10).ToList();
        Assert.Equal(2, joined.Count);
        var bar = joined.OrderByDescending(p => Math.Abs(p[0].X - p[^1].X)).First();
        Assert.InRange(Math.Abs(bar[0].X - bar[^1].X), 105, 125);
        var stem = joined.Single(p => p != bar);
        Assert.InRange(stem.Count, 55, 80);
    }

    [Fact]
    public void NoJunctions_LeavesPathsAlone()
    {
        var mask = Draw(100, 30, (x, y) => x >= 10 && x < 90 && y >= 14 && y < 17);
        var paths = Skel(mask, 100, 30, out var skel);
        Assert.Equal(paths.Count, Skeleton.JoinAtJunctions(paths, skel, 100, 30).Count);
    }

    [Fact]
    public void ThinCrossing_InADigitizedImage_IsTwoRunObjects()
    {
        var img = Synth.Blank(500, 400);
        Synth.Paint(img, Synth.Rect(40, 198, 460, 202), 10, 10, 10);
        Synth.Paint(img, Synth.Rect(248, 40, 252, 360), 10, 10, 10);
        var r = AutoDigitizer.Run(img, new DigitizeOptions { WidthMm = 100, CropToContent = false });
        Assert.Equal(2, r.Objects.OfType<RunObject>().Count());
    }
}

public class ThinHysteresisTests
{
    [Fact]
    public void StrokeWobblingAroundTheLimit_StaysEntirelyThin()
    {
        // Espessura variando de 10 a 16 px em torno do limite de 14 px (raio 7): antes virava linha com remendos grossos.
        int w = 420, h = 80;
        var mask = new bool[w * h];
        for (int x = 20; x < 400; x++)
        {
            double t = 13 + 3 * Math.Sin(x / 22.0);
            for (int y = 0; y < h; y++) if (Math.Abs(y - 40) <= t / 2) mask[y * w + x] = true;
        }
        var thin = Skeleton.ThinPart(mask, w, h, 7);
        int total = mask.Count(m => m), thinCount = thin.Count(t => t);
        Assert.True(thinCount >= total * 0.97, $"fino: {thinCount} de {total}");
    }

    [Fact]
    public void ClearlyThickBlob_IsStillThick()
    {
        int w = 200, h = 200;
        var mask = new bool[w * h];
        for (int y = 40; y < 160; y++) for (int x = 40; x < 160; x++) mask[y * w + x] = true;
        Assert.DoesNotContain(true, Skeleton.ThinPart(mask, w, h, 7));
    }
}

public class StitchEstimateTests
{
    private static (int Estimate, int Actual) Both(RgbaImage img)
    {
        var o = new DigitizeOptions { WidthMm = 100, CropToContent = false };
        var estimate = AutoDigitizer.EstimateStitches(AutoDigitizer.Prepare(img, o), o);
        var design = new EmbDesign();
        design.Objects.AddRange(AutoDigitizer.Run(img, o).Objects);
        return (estimate, design.BuildPattern("t").StitchCount);
    }

    [Fact]
    public void ThinLineArt_IsEstimatedWithinAThird()
    {
        var img = Synth.Blank(400, 300);
        Synth.Paint(img, Synth.Ring(200, 150, 59, 61), 20, 20, 20);
        Synth.Paint(img, Synth.Rect(30, 148, 370, 151), 20, 20, 20);
        var (est, actual) = Both(img);
        Assert.InRange(est, actual * 0.7, actual * 1.3);
    }

    [Fact]
    public void ThickFill_IsEstimatedWithinAThird()
    {
        var img = Synth.Blank(400, 300);
        Synth.Paint(img, Synth.Rect(30, 60, 370, 240), 30, 90, 160);
        var (est, actual) = Both(img);
        Assert.InRange(est, actual * 0.7, actual * 1.3);
    }
}

