using Bordadox.Core.Design;
using Bordadox.Core.Digitize;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.Tests;

/// <summary>Desenha formas simples numa imagem, com borda suavizada (anti-serrilhado), para testar a digitalização.</summary>
internal static class Synth
{
    public static RgbaImage Blank(int w, int h, byte r = 255, byte g = 255, byte b = 255, byte a = 255)
    {
        var img = new RgbaImage(w, h);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) img.Set(x, y, r, g, b, a);
        return img;
    }

    /// <summary>Pinta onde <paramref name="inside"/> devolve cobertura 0..1 (4×4 subamostras por pixel).</summary>
    public static void Paint(RgbaImage img, Func<double, double, bool> inside, byte r, byte g, byte b)
    {
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < 4; sy++)
                    for (int sx = 0; sx < 4; sx++)
                        if (inside(x + (sx + 0.5) / 4, y + (sy + 0.5) / 4)) hits++;
                if (hits == 0) continue;
                double t = hits / 16.0;
                var (pr, pg, pb, pa) = img.Get(x, y);
                if (pa == 0) img.Set(x, y, r, g, b, (byte)Math.Round(255 * t)); // tela transparente: a borda fica meio transparente
                else img.Set(x, y, Mix(pr, r, t), Mix(pg, g, t), Mix(pb, b, t), pa);
            }
    }

    private static byte Mix(byte a, byte b, double t) => (byte)Math.Round(a + (b - a) * t);

    public static Func<double, double, bool> Rect(double x0, double y0, double x1, double y1) =>
        (x, y) => x >= x0 && x < x1 && y >= y0 && y < y1;

    public static Func<double, double, bool> Disc(double cx, double cy, double r) =>
        (x, y) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;

    public static Func<double, double, bool> Ring(double cx, double cy, double r0, double r1) =>
        (x, y) => { double d2 = (x - cx) * (x - cx) + (y - cy) * (y - cy); return d2 <= r1 * r1 && d2 >= r0 * r0; };
}

public class QuantizerTests
{
    [Fact]
    public void Resize_KeepsFlatColorAndAveragesEdges()
    {
        var img = Synth.Blank(40, 40, 10, 20, 30);
        var small = img.Resize(10, 10);
        Assert.Equal((10, 20, 30, 255), small.Get(5, 5));
        var big = img.Resize(100, 100);
        Assert.Equal((10, 20, 30, 255), big.Get(50, 50));

        var half = Synth.Blank(2, 1);
        half.Set(0, 0, 0, 0, 0); // um pixel preto e um branco
        var one = half.Resize(1, 1);
        Assert.InRange(one.Get(0, 0).R, 126, 129);
    }

    [Fact]
    public void TwoShapesOnWhite_GiveTwoColorsAndNoGhostEdgeColor()
    {
        var img = Synth.Blank(300, 200);
        Synth.Paint(img, Synth.Rect(20, 30, 140, 170), 200, 30, 30);
        Synth.Paint(img, Synth.Disc(220, 100, 60), 30, 60, 190);

        var q = Quantizer.Run(img, new QuantizeOptions { MaxColors = 8 });

        Assert.Equal(2, q.Colors.Count); // vermelho e azul; branco é fundo; sem cor "intermediária" nas bordas
        Assert.NotNull(q.Background);
        Assert.True(q.Background!.Value.R > 240 && q.Background.Value.G > 240);

        int red = q.Labels.Count(l => l == LabelOf(q, 200, 30, 30));
        Assert.InRange(red, 120 * 140 * 0.97, 120 * 140 * 1.03);
        int blue = q.Labels.Count(l => l == LabelOf(q, 30, 60, 190));
        Assert.InRange(blue, Math.PI * 60 * 60 * 0.96, Math.PI * 60 * 60 * 1.04);
        Assert.Equal(-1, q.Labels[5 * 300 + 5]); // canto: fundo
    }

    private static int LabelOf(QuantizedImage q, int r, int g, int b)
    {
        int best = 0;
        double bd = double.MaxValue;
        for (int i = 0; i < q.Colors.Count; i++)
        {
            var c = q.Colors[i];
            double d = Math.Pow(c.R - r, 2) + Math.Pow(c.G - g, 2) + Math.Pow(c.B - b, 2);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    [Fact]
    public void TransparentBackground_IsRemoved()
    {
        var img = Synth.Blank(120, 120, 0, 0, 0, a: 0);
        Synth.Paint(img, Synth.Disc(60, 60, 40), 20, 140, 60);

        var q = Quantizer.Run(img, new QuantizeOptions());
        Assert.True(q.HasTransparency);
        Assert.Single(q.Colors);
        Assert.Equal(-1, q.Labels[0]);
        Assert.Equal(0, q.Labels[60 * 120 + 60]);
    }

    [Fact]
    public void EnclosedBackgroundColor_BecomesFabric_OnlyWhenAsked()
    {
        var img = Synth.Blank(200, 200);
        Synth.Paint(img, Synth.Ring(100, 100, 40, 80), 180, 20, 20);

        var withHole = Quantizer.Run(img, new QuantizeOptions { RemoveEnclosedBackground = true });
        Assert.Equal(-1, withHole.Labels[100 * 200 + 100]);   // miolo do anel: tecido
        Assert.Single(withHole.Colors);

        var kept = Quantizer.Run(img, new QuantizeOptions { RemoveEnclosedBackground = false });
        Assert.Equal(2, kept.Colors.Count);                    // miolo continua branco (costurado)
        Assert.NotEqual(-1, kept.Labels[100 * 200 + 100]);
        Assert.Equal(-1, kept.Labels[3 * 200 + 3]);            // o fundo da borda sai nos dois casos
    }

    [Fact]
    public void NoisyJpegLikeImage_StillGivesCleanColors()
    {
        var img = Synth.Blank(240, 160);
        Synth.Paint(img, Synth.Rect(30, 30, 210, 130), 40, 120, 60);
        var rng = new Random(7);
        for (int i = 0; i < img.Data.Length; i += 4)
            for (int c = 0; c < 3; c++)
                img.Data[i + c] = (byte)Math.Clamp(img.Data[i + c] + rng.Next(-5, 6), 0, 255); // ruído de compressão

        var q = Quantizer.Run(img, new QuantizeOptions { MaxColors = 8 });
        Assert.Single(q.Colors);
        Assert.Equal(-1, q.Labels[10 * 240 + 10]);
        Assert.Equal(0, q.Labels[80 * 240 + 120]);
    }

    [Fact]
    public void FixedColorCount_IsRespected()
    {
        var img = Synth.Blank(200, 100);
        Synth.Paint(img, Synth.Rect(0, 0, 50, 100), 255, 0, 0);
        Synth.Paint(img, Synth.Rect(50, 0, 100, 100), 0, 255, 0);
        Synth.Paint(img, Synth.Rect(100, 0, 150, 100), 0, 0, 255);
        Synth.Paint(img, Synth.Rect(150, 0, 200, 100), 0, 0, 0);
        var q = Quantizer.Run(img, new QuantizeOptions { MaxColors = 4, Auto = false, RemoveBackground = false });
        Assert.Equal(4, q.Colors.Count);
    }
}

public class TracerTests
{
    private static bool[] Mask(string[] rows)
    {
        int h = rows.Length, w = rows[0].Length;
        var m = new bool[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) m[y * w + x] = rows[y][x] == '#';
        return m;
    }

    [Fact]
    public void SinglePixel_IsASquareWithPositiveArea()
    {
        var shapes = ContourTracer.Trace(Mask(new[] { "#" }), 1, 1);
        var s = Assert.Single(shapes);
        Assert.Equal(1, s.Area);
        Assert.Equal(4, s.Outer.Count);
        Assert.Empty(s.Holes);
    }

    [Fact]
    public void Ring_HasOneOuterAndOneHole()
    {
        var rows = new[]
        {
            "#####",
            "#...#",
            "#...#",
            "#...#",
            "#####",
        };
        var s = Assert.Single(ContourTracer.Trace(Mask(rows), 5, 5));
        Assert.Equal(16, s.Area);
        Assert.Single(s.Holes);
        Assert.Equal(4, s.Outer.Count);
        Assert.Equal(4, s.Holes[0].Count);
    }

    [Fact]
    public void DiagonalPixels_AreSeparateShapes()
    {
        var shapes = ContourTracer.Trace(Mask(new[] { "#.", ".#" }), 2, 2);
        Assert.Equal(2, shapes.Count);
        Assert.All(shapes, s => Assert.Equal(1, s.Area));
    }

    [Fact]
    public void TwoSeparateBlobs_AndAnIslandInsideAHole()
    {
        var rows = new[]
        {
            "#######..##",
            "#.....#..##",
            "#.###.#....",
            "#.###.#....",
            "#.....#....",
            "#######....",
        };
        var shapes = ContourTracer.Trace(Mask(rows), 11, 6);
        Assert.Equal(3, shapes.Count); // moldura, ilha de dentro e o bloco do canto
        var frame = shapes.Single(s => s.Holes.Count == 1);
        Assert.Equal(22, frame.Area);                                  // 7 + 2 + 2 + 2 + 2 + 7 pixels
        Assert.Contains(shapes, s => s.Area == 6 && s.Holes.Count == 0); // a ilha 3 × 2 dentro do miolo
        Assert.Contains(shapes, s => s.Area == 4 && s.Holes.Count == 0); // o bloco 2 × 2 do canto
    }

    [Fact]
    public void Loops_CoverExactlyTheMaskArea()
    {
        // Área do contorno externo menos furos (pela fórmula do polígono) = nº de pixels, para qualquer forma.
        var img = Synth.Blank(120, 90);
        Synth.Paint(img, Synth.Ring(60, 45, 15, 38), 0, 0, 0);
        Synth.Paint(img, Synth.Rect(5, 5, 20, 30), 0, 0, 0);
        var mask = new bool[120 * 90];
        for (int i = 0; i < mask.Length; i++) mask[i] = img.Data[i * 4] < 128;

        var shapes = ContourTracer.Trace(mask, 120, 90);
        double polyArea = 0;
        foreach (var s in shapes)
        {
            polyArea += Math.Abs(GeometryUtil.SignedArea(s.Outer.Select(p => new Vec2(p.X, p.Y)).ToList()));
            foreach (var hole in s.Holes) polyArea -= Math.Abs(GeometryUtil.SignedArea(hole.Select(p => new Vec2(p.X, p.Y)).ToList()));
        }
        Assert.Equal(mask.Count(m => m), polyArea, 3);
        Assert.Equal(mask.Count(m => m), shapes.Sum(s => s.Area));
    }
}

public class VectorizerTests
{
    private static Vec2 Same(Vec2 p) => p;

    [Fact]
    public void Circle_BecomesAFewHundredthsMmCloseSmoothOutline()
    {
        int size = 200;
        var mask = new bool[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                mask[y * size + x] = (x + 0.5 - 100) * (x + 0.5 - 100) + (y + 0.5 - 100) * (y + 0.5 - 100) <= 70 * 70;

        var shape = Assert.Single(ContourTracer.Trace(mask, size, size));
        var nodes = Vectorizer.Fit(shape.Outer, 1.0, 30, Same);

        Assert.InRange(nodes.Count, 8, 120); // muito menos que os ~400 cantos da escada
        // Todos os nós ficam sobre o círculo (±1 pixel) e a curva suave também.
        Assert.All(nodes, n => Assert.InRange(Vec2.Distance(n.P, new Vec2(100, 100)), 70 - 1.2, 70 + 1.2));
        var flat = PathUtil.Flatten(nodes, true);
        Assert.All(flat, p => Assert.InRange(Vec2.Distance(p, new Vec2(100, 100)), 70 - 1.6, 70 + 1.6));
    }

    [Fact]
    public void Rectangle_KeepsFourSharpCorners()
    {
        var mask = new bool[100 * 60];
        for (int y = 10; y < 50; y++) for (int x = 20; x < 80; x++) mask[y * 100 + x] = true;
        var shape = Assert.Single(ContourTracer.Trace(mask, 100, 60));
        var nodes = Vectorizer.Fit(shape.Outer, 1.0, 30, Same);
        Assert.Equal(4, nodes.Count);
        Assert.All(nodes, n => Assert.False(n.Curve));
    }

    [Fact]
    public void DiagonalEdge_StairsCollapseToOneStraightLine()
    {
        var size = 120;
        var mask = new bool[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                mask[y * size + x] = x + y < 130 && x > 10 && y > 10;   // triângulo com hipotenusa diagonal
        var shape = Assert.Single(ContourTracer.Trace(mask, size, size));
        Assert.True(shape.Outer.Count > 100); // escada de pixels
        var nodes = Vectorizer.Fit(shape.Outer, 1.0, 30, Same);
        Assert.InRange(nodes.Count, 3, 5);    // vira um triângulo
    }
}

public class AutoDigitizerTests
{
    private static ThreadColor Thread(int idx) => BrotherPalette.Colors[idx];

    // Os testes de posição e escala usam a imagem inteira (sem recorte); o recorte tem testes próprios.
    private static DigitizeResult Run(RgbaImage img, Action<DigitizeOptions>? tweak = null, double widthMm = 100)
    {
        var o = new DigitizeOptions { WidthMm = widthMm, CropToContent = false };
        tweak?.Invoke(o);
        return AutoDigitizer.Run(img, o);
    }

    [Fact]
    public void CropToContent_MakesWidthApplyToTheDrawingNotTheCanvas()
    {
        var img = Synth.Blank(800, 600);                               // muita margem branca
        Synth.Paint(img, Synth.Rect(300, 250, 500, 350), 20, 120, 50); // desenho de 200 × 100 px no meio

        var cropped = AutoDigitizer.CropToContent(img, new QuantizeOptions());
        Assert.InRange(cropped.Width, 200, 230);   // 200 + folga de ~1,5% de cada lado
        Assert.InRange(cropped.Height, 100, 150);

        var r = AutoDigitizer.Run(img, new DigitizeOptions { WidthMm = 80 });   // recorte ligado por padrão
        var fill = Assert.IsType<FillObject>(Assert.Single(r.Objects));
        Assert.InRange(fill.Bounds().Width, 70, 80);   // o desenho (≈ 90% da largura recortada) tem ~72–78 mm
        Assert.Equal(0, fill.Bounds().Center.X, 1.5);   // e continua centrado
        Assert.Equal(0, fill.Bounds().Center.Y, 1.5);
    }

    [Fact]
    public void CropToContent_LeavesImageAloneWhenThereIsNoMargin()
    {
        var img = Synth.Blank(200, 100, 30, 90, 200);   // cor única, sem fundo distinto
        Assert.Same(img, AutoDigitizer.CropToContent(img, new QuantizeOptions()));
    }

    [Fact]
    public void CropToContent_IgnoresTinySpecksFarFromTheDrawing()
    {
        var img = Synth.Blank(800, 600);
        Synth.Paint(img, Synth.Rect(300, 250, 500, 350), 20, 120, 50);
        Synth.Paint(img, Synth.Rect(10, 10, 13, 13), 20, 120, 50);     // poeirinha no canto
        var cropped = AutoDigitizer.CropToContent(img, new QuantizeOptions());
        Assert.True(cropped.Width < 300, $"a poeira não pode aumentar o recorte (largura {cropped.Width})");
    }

    [Fact]
    public void RedSquareAndBlueDisc_GiveTwoFillsInTheRightPlace()
    {
        var img = Synth.Blank(400, 300);
        Synth.Paint(img, Synth.Rect(40, 60, 190, 240), 230, 25, 30);   // 150 × 180 px = 37,5 × 45 mm
        Synth.Paint(img, Synth.Disc(290, 150, 80), 12, 85, 165);       // raio 80 px = 20 mm

        var r = Run(img);
        Assert.Equal(100, r.WidthMm);
        Assert.Equal(75, r.HeightMm, 3);
        Assert.Equal(2, r.Layers.Count);
        var fills = r.Objects.Cast<FillObject>().ToList();
        Assert.Equal(2, fills.Count);

        var red = fills.Single(f => f.Color.R > 150);
        var blue = fills.Single(f => f.Color.B > 100);
        // O desenho fica centrado em (0,0): 400 px = 100 mm (4 px/mm), imagem de 100 × 75 mm.
        var rb = red.Bounds();
        Assert.Equal((40 - 200) / 4.0, rb.MinX, 0.5);   // −40 mm
        Assert.Equal(37.5, rb.Width, 0.5);
        Assert.Equal(45, rb.Height, 0.5);
        Assert.Equal(0, rb.Center.Y, 0.5);               // o quadrado vai de y = 60 a 240 px, centro da imagem
        var bb = blue.Bounds();
        Assert.Equal(40, bb.Width, 0.5);
        Assert.Equal(40, bb.Height, 0.5);
        Assert.Equal((290 - 200) / 4.0, bb.Center.X, 0.5);
        Assert.Equal(0, bb.Center.Y, 0.5);
    }

    [Fact]
    public void Ring_KeepsItsHoleAndNoStitchFallsInsideIt()
    {
        var img = Synth.Blank(300, 300);
        Synth.Paint(img, Synth.Ring(150, 150, 50, 110), 20, 120, 50);
        var r = Run(img, o => { o.Quantize.RemoveEnclosedBackground = true; });
        var fill = Assert.IsType<FillObject>(Assert.Single(r.Objects));
        Assert.Single(fill.Holes);

        var design = new EmbDesign();
        design.Objects.Add(fill);
        var p = design.BuildPattern();
        double holeRadiusMm = 50 / 3.0; // 300 px = 100 mm → 3 px/mm
        foreach (var s in p.Stitches.Where(s => s.Command == StitchCommand.Stitch))
        {
            double d = Math.Sqrt(Math.Pow(s.X / 10.0, 2) + Math.Pow(s.Y / 10.0, 2));
            Assert.True(d > holeRadiusMm - 0.8, $"ponto dentro do furo: r = {d:0.0} mm");
        }
        Assert.InRange(p.StitchCount, 500, 12000);
    }

    [Fact]
    public void TouchingColors_OverlapSoNoFabricShowsBetween()
    {
        var img = Synth.Blank(300, 200);
        Synth.Paint(img, Synth.Rect(20, 20, 160, 180), 200, 30, 30);   // vermelho (maior: vai por baixo)
        Synth.Paint(img, Synth.Rect(160, 20, 280, 180), 20, 40, 200);  // azul, encostado
        var r = Run(img);
        Assert.Equal(2, r.Layers.Count);

        var lower = r.Objects.Cast<FillObject>().First();   // costurado primeiro
        var upper = r.Objects.Cast<FillObject>().Last();
        Assert.True(lower.Color.R > 150);
        // O de baixo avança por baixo do de cima: ~0,25 mm (a escala é 3 px/mm).
        double boundary = (160 - 150) / 3.0;
        Assert.InRange(lower.Bounds().MaxX, boundary + 0.15, boundary + 0.6);
        Assert.InRange(upper.Bounds().MinX, boundary - 0.15, boundary + 0.15);
    }

    [Fact]
    public void BigAreasUseTatami_NarrowStripsUseSatinAcrossTheirWidth()
    {
        var img = Synth.Blank(400, 300);
        Synth.Paint(img, Synth.Rect(20, 20, 200, 200), 30, 30, 30);     // grande
        Synth.Paint(img, Synth.Rect(230, 30, 245, 270), 200, 150, 20);  // faixa vertical de 15 px (≈ 3,75 mm → satin se ≤ 3,5)
        var r = Run(img, o => { o.SatinMaxWidthMm = 5; });
        var fills = r.Objects.Cast<FillObject>().ToList();
        var big = fills.Single(f => f.Color.R < 80);
        var strip = fills.Single(f => f.Color.R > 150);
        Assert.Equal(FillType.Tatami, big.FillType);
        Assert.Equal(FillType.Satin, strip.FillType);
        // Faixa vertical: pontos horizontais (ângulo ≈ 0° ou 180°), cruzando o lado estreito.
        Assert.True(Math.Abs(Math.Abs(strip.Angle) % 180) < 10 || Math.Abs(Math.Abs(strip.Angle) % 180 - 180) < 10, $"ângulo {strip.Angle}");
    }

    [Fact]
    public void FillModeOverrides_AndTinyDetailsAreDropped()
    {
        var img = Synth.Blank(400, 300);
        Synth.Paint(img, Synth.Rect(20, 20, 200, 200), 30, 30, 30);
        Synth.Paint(img, Synth.Disc(300, 100, 1), 200, 30, 30);         // pontinho de 0,5 mm de diâmetro
        var tatami = Run(img, o => { o.FillMode = FillMode.Tatami; o.MinDetailMm = 1; });
        Assert.All(tatami.Objects.Cast<FillObject>(), f => Assert.Equal(FillType.Tatami, f.FillType));
        Assert.Single(tatami.Objects);                                  // o pontinho sumiu

        var satin = Run(img, o => { o.FillMode = FillMode.Satin; o.MinDetailMm = 1; });
        Assert.All(satin.Objects.Cast<FillObject>(), f => Assert.Equal(FillType.Satin, f.FillType));
    }

    [Fact]
    public void ColorsThatMapToTheSameThread_AreMerged()
    {
        var img = Synth.Blank(300, 150);
        Synth.Paint(img, Synth.Rect(10, 10, 140, 140), 0, 0, 0);
        Synth.Paint(img, Synth.Rect(160, 10, 290, 140), 8, 6, 10);       // quase igual: mesma linha Brother
        var r = Run(img, o => { o.Quantize.Auto = false; o.Quantize.MaxColors = 3; });
        Assert.Single(r.Layers);
        Assert.Equal(2, r.Objects.Count); // dois objetos da mesma cor
        Assert.Single(r.Objects.Select(o => o.Color).Distinct());
    }

    [Fact]
    public void Cancellation_StopsTheWork()
    {
        var img = Synth.Blank(400, 300);
        Synth.Paint(img, Synth.Disc(200, 150, 100), 10, 10, 200);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() => AutoDigitizer.Run(img, new DigitizeOptions(), null, cts.Token));
    }

    [Fact]
    public void Progress_IsReportedUpToOne()
    {
        var img = Synth.Blank(200, 150);
        Synth.Paint(img, Synth.Rect(20, 20, 180, 130), 10, 160, 10);
        var seen = new List<double>();
        AutoDigitizer.Run(img, new DigitizeOptions(), new Progress1(seen));
        Assert.NotEmpty(seen);
        Assert.Equal(1, seen[^1]);
        Assert.Equal(seen.OrderBy(x => x), seen);
    }

    private sealed class Progress1(List<double> list) : IProgress<DigitizeProgress>
    {
        public void Report(DigitizeProgress value) => list.Add(value.Fraction);
    }
}
