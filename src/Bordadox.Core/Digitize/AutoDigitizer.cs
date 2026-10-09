using Bordadox.Core.Design;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.Core.Digitize;

public enum FillMode
{
    /// <summary>Satin nas partes estreitas, tatami nas largas.</summary>
    Auto,
    Tatami,
    Satin,
}

public sealed class DigitizeOptions
{
    /// <summary>Largura final do desenho; a altura segue a proporção da imagem.</summary>
    public double WidthMm { get; set; } = 100;

    public QuantizeOptions Quantize { get; } = new();

    /// <summary>Detalhes menores que isto (em mm) são ignorados.</summary>
    public double MinDetailMm { get; set; } = 0.5;

    /// <summary>Partes mais finas que isto (em mm) não são preenchidas.</summary>
    public double MinWidthMm { get; set; } = 0.45;

    public FillMode FillMode { get; set; } = FillMode.Auto;

    /// <summary>Une os tons de um mesmo degradê (ex.: dourado metálico) numa cor só. Ver <see cref="ShadeMerger"/>.</summary>
    public bool MergeShades { get; set; } = true;

    /// <summary>Quanto cada cor de baixo avança por baixo da cor de cima, para não aparecer tecido entre elas.</summary>
    public double OverlapMm { get; set; } = 0.25;

    /// <summary>Resolução de trabalho. Limitada para o maior lado não passar de 1500 pixels.</summary>
    public double PixelsPerMm { get; set; } = 12;

    /// <summary>Quanto o contorno vetorizado pode se afastar da imagem (mm).</summary>
    public double SimplifyMm { get; set; } = 0.12;

    /// <summary>Largura máxima (mm) de uma região para ser costurada em satin no modo automático.</summary>
    public double SatinMaxWidthMm { get; set; } = 3.5;

    /// <summary>Corta a margem vazia (fundo) em volta do desenho: a largura em mm vale para o desenho, não para a imagem.</summary>
    public bool CropToContent { get; set; } = true;

    /// <summary>
    /// Traços mais finos que <see cref="ThinMaxMm"/> viram linha de ponto corrido em vez de preenchimento.
    /// Logos de linha fina (fios de menos de ~1 mm) dependem disto; formas grossas continuam sendo preenchidas.
    /// </summary>
    public bool ThinLines { get; set; } = true;

    /// <summary>Espessura (mm) abaixo da qual um traço é "fino".</summary>
    public double ThinMaxMm { get; set; } = 1.2;

    /// <summary>Linhas mais curtas que isto (mm) são descartadas.</summary>
    public double MinLineMm { get; set; } = 1.2;

    /// <summary>Pontas curtas que saem de uma bifurcação (resto da afinação) menores que isto (mm) são descartadas.</summary>
    public double SpurMm { get; set; } = 1.5;
}

public sealed record DigitizeProgress(string Stage, double Fraction);

public sealed class DigitizeLayer
{
    public ThreadColor Thread { get; init; } = BrotherPalette.Colors[1];
    public int ObjectCount { get; set; }
    public double AreaMm2 { get; set; }
}

public sealed class DigitizeResult
{
    public List<EmbObject> Objects { get; } = new();
    public List<DigitizeLayer> Layers { get; } = new();
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    /// <summary>Regiões descartadas por serem pequenas demais ou finas demais.</summary>
    public int SkippedTiny { get; set; }
    public int SkippedThin { get; set; }
    /// <summary>Quantas linhas de ponto corrido (traços finos) foram criadas.</summary>
    public int ThinLines { get; set; }
    public (byte R, byte G, byte B)? Background { get; set; }
}

public static class AutoDigitizer
{
    private static readonly double[] TatamiAngles = { 45, 135, 15, 105, 75, 165 };

    /// <summary>
    /// Corta a margem de fundo em volta do desenho. Acha o fundo com a mesma redução de cores da digitalização,
    /// numa versão pequena da imagem, e devolve só o retângulo com conteúdo (mais uma folga de ~1,5%).
    /// Devolve a própria imagem se não achar fundo ou se não houver margem.
    /// </summary>
    public static RgbaImage CropToContent(RgbaImage source, QuantizeOptions qo, CancellationToken ct = default)
    {
        var small = source.Limit(320);
        var q = Quantizer.Run(small, qo, ct);
        int w = small.Width, h = small.Height;
        LabelCleaner.RemoveSmall(q.Labels, w, h, Math.Max(4, w * h / 20000), ct); // manchinhas não podem aumentar o recorte

        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (q.Labels[y * w + x] >= 0)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) return source;

        int pad = Math.Max(1, (int)Math.Round(Math.Max(w, h) * 0.015));
        minX = Math.Max(0, minX - pad); minY = Math.Max(0, minY - pad);
        maxX = Math.Min(w - 1, maxX + pad); maxY = Math.Min(h - 1, maxY + pad);
        if (minX == 0 && minY == 0 && maxX == w - 1 && maxY == h - 1) return source;

        double sx = (double)source.Width / w, sy = (double)source.Height / h;
        int x0 = (int)Math.Floor(minX * sx), y0 = (int)Math.Floor(minY * sy);
        int x1 = (int)Math.Ceiling((maxX + 1) * sx), y1 = (int)Math.Ceiling((maxY + 1) * sy);
        return source.Crop(x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>A imagem já no tamanho de trabalho, com uma cor Brother por pixel (−1 = sem ponto).</summary>
    public sealed class PreparedImage
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public double PixelsPerMm { get; init; }
        public double WidthMm { get; init; }
        public double HeightMm { get; init; }
        public int[] Labels { get; init; } = Array.Empty<int>();
        public List<ThreadColor> Threads { get; init; } = new();
        public (byte R, byte G, byte B)? Background { get; init; }
    }

    /// <summary>
    /// Primeira metade da digitalização: recorta a margem, redimensiona, reduz as cores, liga cada cor a uma linha Brother
    /// e limpa os detalhes pequenos. A prévia da janela usa esta mesma função, então mostra exatamente o que será costurado.
    /// </summary>
    public static PreparedImage Prepare(RgbaImage source, DigitizeOptions o, IProgress<DigitizeProgress>? progress = null, CancellationToken ct = default)
    {
        void Report(string stage, double f) => progress?.Report(new DigitizeProgress(stage, f));

        // 1) Tamanho de trabalho (depois de cortar a margem de fundo).
        if (o.CropToContent) source = CropToContent(source, o.Quantize, ct);
        double aspect = source.Height / (double)source.Width;
        double wMm = Math.Max(5, o.WidthMm), hMm = wMm * aspect;
        double ppm = Math.Clamp(Math.Min(o.PixelsPerMm, 1500 / Math.Max(wMm, hMm)), 2, 20);
        int w = Math.Max(8, (int)Math.Round(wMm * ppm)), h = Math.Max(8, (int)Math.Round(hMm * ppm));
        Report("Preparando a imagem", 0.02);
        var img = source.Resize(w, h);
        ct.ThrowIfCancellationRequested();

        // 2) Cores.
        Report("Reduzindo as cores", 0.08);
        // Imagem ampliada tem borda suavizada larga (1 pixel original vira vários): o filtro de borda acompanha.
        int edgeRadius = Math.Max(1, (int)Math.Round(w / (double)source.Width));
        var q = Quantizer.Run(img, o.Quantize, ct, edgeRadius);
        if (o.MergeShades && o.Quantize.Auto) q = ShadeMerger.Merge(q, img, edgeRadius);
        var labels = q.Labels;
        var threads = MapToThreads(q, labels);

        // 3) Limpeza: detalhes menores que o mínimo somem.
        Report("Limpando detalhes pequenos", 0.25);
        int minArea = Math.Max(4, (int)(o.MinDetailMm * o.MinDetailMm * ppm * ppm * 0.6));
        LabelCleaner.RemoveSmall(labels, w, h, minArea, ct);

        return new PreparedImage
        {
            Width = w, Height = h, PixelsPerMm = ppm, WidthMm = wMm, HeightMm = hMm,
            Labels = labels, Threads = threads, Background = q.Background,
        };
    }


    /// <summary>
    /// Estimativa do número de pontos sem gerar os objetos: o que é fino vira linha (comprimento do esqueleto × pontos por mm
    /// do ponto corrido triplo), o resto é preenchimento (ver <see cref="EstimateFill"/>). Só área daria quase a metade
    /// do valor real em logos de linha fina.
    /// </summary>
    public static int EstimateStitches(PreparedImage prep, DigitizeOptions o, CancellationToken ct = default)
    {
        int w = prep.Width, h = prep.Height, n = w * h;
        double ppm = prep.PixelsPerMm, total = 0;
        int radius = Math.Max(1, (int)Math.Round(o.ThinMaxMm * ppm / 2));
        double perMm = 3.0 / 2.0 * 1.3;   // triplo com ponto de 2,0 mm = 1,5 por mm; +30% de arremates e curvas (medido no logo da borboleta)

        for (int l = 0; l < prep.Threads.Count; l++)
        {
            ct.ThrowIfCancellationRequested();
            var mask = new bool[n];
            int area = 0;
            for (int i = 0; i < n; i++) if (prep.Labels[i] == l) { mask[i] = true; area++; }
            if (area == 0) continue;

            var fillMask = mask;
            int thinPixels = 0;
            double lineMm = 0;
            if (o.ThinLines)
            {
                var thin = Skeleton.ThinPart(mask, w, h, radius, ct);
                thinPixels = thin.Count(t => t);
                if (thinPixels > 0)
                {
                    var skeleton = Skeleton.Thin(thin, w, h, ct);
                    Skeleton.Prune(skeleton, w, h, (int)Math.Ceiling(o.SpurMm * ppm));
                    lineMm = skeleton.Count(s => s) * 1.1 / ppm;   // 1,1: passos na diagonal são mais longos
                    fillMask = new bool[n];
                    for (int i = 0; i < n; i++) fillMask[i] = mask[i] && !thin[i];
                }
            }
            total += lineMm * perMm;
            foreach (var shape in ContourTracer.Trace(fillMask, w, h, ct))
                total += EstimateFill(shape, ppm, o) * 1.12;   // +12%: deslocamentos e arremates entre objetos (medido: -9% a -16% sem isto)
        }
        return (int)Math.Round(total);
    }


    /// <summary>
    /// Pontos de um preenchimento, pelo mesmo critério da digitalização: a largura média (2·área/perímetro) decide satin ou
    /// tatami. Satin dá uma linha de pontos a cada 0,4 mm ao longo da forma (2,5·área/largura); tatami, uma fileira a cada
    /// 0,4 mm com pontos de 3,5 mm (0,71 por mm²).
    /// </summary>
    /// <summary>Comprimento de um contorno em escada de pixels, medido de 4 em 4 pontos (a escada diagonal não conta o dobro).</summary>
    private static double SmoothLength(List<PointI> ring)
    {
        if (ring.Count < 2) return 0;
        const int step = 4;
        double sum = 0;
        for (int i = 0; i < ring.Count; i += step)
        {
            var a = ring[i]; var b = ring[(i + step) % ring.Count];
            sum += Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        }
        return sum;
    }

    private static double EstimateFill(Shape shape, double ppm, DigitizeOptions o)
    {
        double areaMm2 = shape.Area / (ppm * ppm);
        if (areaMm2 < o.MinDetailMm * o.MinDetailMm * 0.7) return 0;
        double perimeterMm = (SmoothLength(shape.Outer) + shape.Holes.Sum(SmoothLength)) / ppm;
        double widthMm = 2 * areaMm2 / Math.Max(1e-6, perimeterMm);
        if (widthMm < o.MinWidthMm) return 0;
        bool satin = o.FillMode switch { FillMode.Satin => true, FillMode.Tatami => false, _ => widthMm <= o.SatinMaxWidthMm };
        bool underlay = areaMm2 > 6 && widthMm > 1.5;
        // Fatores ajustados contando os pontos reais de logos de teste: o satin com base (zigue-zague) leva quase o dobro;
        // no tatami, formas estreitas têm fileiras curtas (cada fileira arredonda para cima os pontos) e a base pesa mais.
        return satin
            ? 2.5 * areaMm2 / widthMm * (underlay ? 1.9 : 1.15)
            : areaMm2 / (0.4 * 3.5) * (1.3 + 3.5 / Math.Max(1.0, widthMm));
    }


    public static DigitizeResult Run(RgbaImage source, DigitizeOptions o, IProgress<DigitizeProgress>? progress = null, CancellationToken ct = default)
    {
        void Report(string stage, double f) => progress?.Report(new DigitizeProgress(stage, f));

        var prep = Prepare(source, o, progress, ct);
        int w = prep.Width, h = prep.Height;
        double ppm = prep.PixelsPerMm, wMm = prep.WidthMm, hMm = prep.HeightMm;
        var labels = prep.Labels;
        var threads = prep.Threads;

        // 4) Camadas: a de maior área vai por baixo; cada uma avança um pouco por baixo das de cima.
        Report("Montando as camadas", 0.35);
        var pixelCount = new int[threads.Count];
        foreach (var l in labels) if (l >= 0) pixelCount[l]++;
        var order = Enumerable.Range(0, threads.Count).Where(i => pixelCount[i] > 0).OrderByDescending(i => pixelCount[i]).ToList();

        var result = new DigitizeResult { WidthMm = wMm, HeightMm = hMm, Background = prep.Background };
        var masks = BuildLayerMasks(labels, order, w, h, Math.Max(1, (int)Math.Round(o.OverlapMm * ppm)), ct);

        // 5) Contornos → objetos.
        Vec2 ToMm(Vec2 p) => new((p.X - w / 2.0) / ppm, (p.Y - h / 2.0) / ppm);
        double tolPx = Math.Max(0.7, o.SimplifyMm * ppm);
        Vec2? last = null;

        for (int li = 0; li < order.Count; li++)
        {
            ct.ThrowIfCancellationRequested();
            Report($"Vetorizando a cor {li + 1} de {order.Count}", 0.4 + 0.55 * li / order.Count);

            var thread = threads[order[li]];
            var layer = new DigitizeLayer { Thread = thread };
            var made = new List<(EmbObject Obj, Vec2 Center)>();

            // Traços finos (fios, contornos, letras finas) não se preenchem: viram linha de ponto corrido no centro do traço.
            var fillMask = masks[li];
            if (o.ThinLines)
            {
                int radius = Math.Max(1, (int)Math.Round(o.ThinMaxMm * ppm / 2));
                var thin = Skeleton.ThinPart(masks[li], w, h, radius, ct);
                int thinPixels = thin.Count(t => t);
                if (thinPixels > 0)
                {
                    var skeleton = Skeleton.Thin(thin, w, h, ct);
                    Skeleton.Prune(skeleton, w, h, (int)Math.Ceiling(o.SpurMm * ppm));
                    foreach (var line in MakeLines(Skeleton.JoinAtJunctions(Skeleton.Trace(skeleton, w, h, ct), skeleton, w, h, Math.Max(3, radius), 2 * Math.Max(3, radius) + 2), skeleton, w, h, ppm, ToMm, o, thread))
                    {
                        made.Add(line);
                        result.ThinLines++;
                    }
                    layer.AreaMm2 += thinPixels / (ppm * ppm);
                    fillMask = new bool[masks[li].Length];
                    for (int i = 0; i < fillMask.Length; i++) fillMask[i] = masks[li][i] && !thin[i];
                }
            }

            foreach (var shape in ContourTracer.Trace(fillMask, w, h, ct))
            {
                double areaMm2 = shape.Area / (ppm * ppm);
                if (areaMm2 < o.MinDetailMm * o.MinDetailMm * 0.7) { result.SkippedTiny++; continue; }

                var outer = Vectorizer.Fit(shape.Outer, tolPx, 30, ToMm);
                if (outer.Count < 3) { result.SkippedTiny++; continue; }

                var holes = new List<List<PathNode>>();
                double perimeter = Vectorizer.Perimeter(outer);
                foreach (var hole in shape.Holes)
                {
                    var nodes = Vectorizer.Fit(hole, tolPx, 30, ToMm);
                    if (nodes.Count < 3 || Vectorizer.Area(nodes) < o.MinDetailMm * o.MinDetailMm * 0.7) continue;
                    holes.Add(nodes);
                    perimeter += Vectorizer.Perimeter(nodes);
                }

                double widthMm = perimeter > 0 ? 2 * areaMm2 / perimeter : 0;
                if (widthMm < o.MinWidthMm) { result.SkippedThin++; continue; }

                var fill = MakeFill(outer, holes, widthMm, areaMm2, li, o, thread);
                made.Add((fill, Centroid(outer)));
                layer.AreaMm2 += areaMm2;
            }

            // Ordem de costura dentro da cor: sempre o objeto mais perto do último ponto, para saltar menos.
            while (made.Count > 0)
            {
                int pick = 0;
                if (last is { } l)
                {
                    double best = double.MaxValue;
                    for (int i = 0; i < made.Count; i++)
                    {
                        double d = Vec2.Distance(l, made[i].Center);
                        if (d < best) { best = d; pick = i; }
                    }
                }
                var (obj, center) = made[pick];
                made.RemoveAt(pick);
                obj.Name = $"{thread.Name} {layer.ObjectCount + 1}";
                result.Objects.Add(obj);
                layer.ObjectCount++;
                last = center;
            }
            if (layer.ObjectCount > 0) result.Layers.Add(layer);
        }

        Report("Pronto", 1);
        return result;
    }

    /// <summary>Caminhos do esqueleto → objetos de ponto corrido, sem as pontinhas (espinhos) e os fiapos curtos.</summary>
    private static IEnumerable<(EmbObject, Vec2)> MakeLines(List<List<PointI>> paths, bool[] skeleton, int w, int h, double ppm,
        Func<Vec2, Vec2> toMm, DigitizeOptions o, ThreadColor thread)
    {
        int Degree(PointI p) => Skeleton.Branches(skeleton, w, h, p.X, p.Y); // ramos, não vizinhos

        double tol = Math.Max(0.8, 0.08 * ppm);
        foreach (var path in paths)
        {
            double lenMm = 0;
            for (int i = 1; i < path.Count; i++)
                lenMm += Math.Sqrt(Math.Pow(path[i].X - path[i - 1].X, 2) + Math.Pow(path[i].Y - path[i - 1].Y, 2)) / ppm;
            bool closed = path.Count > 3 && path[0] == path[^1];

            if (lenMm < o.MinLineMm) continue; // fiapo
            if (!closed)
            {
                // Espinho: ponta livre que sai de uma bifurcação e é curta (resto do esqueleto, não parte do desenho).
                int d0 = Degree(path[0]), d1 = Degree(path[^1]);
                bool spur = (d0 >= 3 && d1 <= 1) || (d1 >= 3 && d0 <= 1);
                if (spur && lenMm < o.SpurMm) continue;
            }

            var pts = closed ? path.Take(path.Count - 1).ToList() : path;
            var nodes = Vectorizer.FitOpen(pts, tol, 35, toMm);
            if (nodes.Count < 2) continue;
            var run = new RunObject { Color = thread, Nodes = nodes, Closed = closed && nodes.Count >= 3, Triple = true, StitchLength = 2.0 };
            yield return (run, new Vec2(nodes.Average(n => n.P.X), nodes.Average(n => n.P.Y)));
        }
    }

    /// <summary>Liga cada cor da imagem à linha Brother mais próxima e une as cores que caem na mesma linha.</summary>
    public static List<ThreadColor> MapToThreads(QuantizedImage q, int[] labels)
    {
        var threads = new List<ThreadColor>();
        var byIndex = new Dictionary<int, int>();
        var remap = new int[q.Colors.Count];
        for (int i = 0; i < q.Colors.Count; i++)
        {
            var (r, g, b) = q.Colors[i];
            int idx = BrotherPalette.NearestIndex(new ThreadColor(r, g, b, "", ""));
            if (!byIndex.TryGetValue(idx, out var newLabel))
            {
                newLabel = threads.Count;
                byIndex[idx] = newLabel;
                threads.Add(BrotherPalette.Colors[idx]);
            }
            remap[i] = newLabel;
        }
        for (int i = 0; i < labels.Length; i++)
            if (labels[i] >= 0) labels[i] = remap[labels[i]];
        return threads;
    }

    /// <summary>
    /// Máscara de cada camada: a própria cor mais uma faixa fina que entra por baixo das camadas de cima
    /// (as de cima são costuradas depois e cobrem essa faixa). Assim não sobra fresta de tecido entre cores.
    /// </summary>
    private static List<bool[]> BuildLayerMasks(int[] labels, List<int> order, int w, int h, int overlapPx, CancellationToken ct)
    {
        int n = w * h;
        var masks = new bool[order.Count][];
        var upper = new bool[n];
        for (int li = order.Count - 1; li >= 0; li--)
        {
            ct.ThrowIfCancellationRequested();
            var own = new bool[n];
            for (int i = 0; i < n; i++) own[i] = labels[i] == order[li];
            var mask = (bool[])own.Clone();
            if (li < order.Count - 1)
            {
                var grown = LabelCleaner.Dilate(own, w, h, overlapPx);
                for (int i = 0; i < n; i++) if (grown[i] && upper[i]) mask[i] = true;
            }
            masks[li] = mask;
            for (int i = 0; i < n; i++) if (own[i]) upper[i] = true;
        }
        return masks.ToList();
    }

    private static FillObject MakeFill(List<PathNode> outer, List<List<PathNode>> holes, double widthMm, double areaMm2, int layerIndex,
        DigitizeOptions o, ThreadColor thread)
    {
        bool satin = o.FillMode switch
        {
            FillMode.Satin => true,
            FillMode.Tatami => false,
            _ => widthMm <= o.SatinMaxWidthMm,
        };

        double angle = satin
            ? NormalizeAngle(MainAxisDeg(outer) + 90)   // pontos cruzam o lado estreito
            : TatamiAngles[layerIndex % TatamiAngles.Length];

        return new FillObject
        {
            Color = thread,
            Outline = outer,
            Holes = holes,
            FillType = satin ? FillType.Satin : FillType.Tatami,
            Angle = angle,
            Spacing = 0.4,
            StitchLength = 3.5,
            PullCompensation = satin ? 0.15 : 0.2,
            Underlay = areaMm2 > 6 && widthMm > 1.5,
        };
    }

    /// <summary>Direção em que a forma é mais comprida (graus), por análise de componentes principais do contorno.</summary>
    private static double MainAxisDeg(IReadOnlyList<PathNode> outline)
    {
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, n = 0;
        for (int i = 0; i < outline.Count; i++)
        {
            var a = outline[i].P;
            var b = outline[(i + 1) % outline.Count].P;
            int steps = Math.Max(1, (int)Math.Ceiling(Vec2.Distance(a, b) / 0.5));
            for (int k = 0; k < steps; k++)
            {
                var p = Vec2.Lerp(a, b, (double)k / steps);
                sx += p.X; sy += p.Y; sxx += p.X * p.X; syy += p.Y * p.Y; sxy += p.X * p.Y; n++;
            }
        }
        if (n < 2) return 0;
        double mx = sx / n, my = sy / n;
        double cxx = sxx / n - mx * mx, cyy = syy / n - my * my, cxy = sxy / n - mx * my;
        return 0.5 * Math.Atan2(2 * cxy, cxx - cyy) * 180 / Math.PI;
    }

    private static double NormalizeAngle(double a)
    {
        a %= 360;
        if (a > 180) a -= 360;
        if (a <= -180) a += 360;
        return a;
    }

    private static Vec2 Centroid(IReadOnlyList<PathNode> nodes) =>
        new(nodes.Average(n => n.P.X), nodes.Average(n => n.P.Y));
}
