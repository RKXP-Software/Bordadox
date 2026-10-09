namespace Bordadox.Core.Digitize;

public sealed class QuantizeOptions
{
    /// <summary>Número máximo de cores (com <see cref="Auto"/>) ou exato (sem).</summary>
    public int MaxColors { get; set; } = 8;

    /// <summary>Une cores parecidas e descarta cores raras, ficando só com as que a imagem realmente usa.</summary>
    public bool Auto { get; set; } = true;

    /// <summary>Tira a cor de fundo (borda dominante da imagem) e as áreas transparentes.</summary>
    public bool RemoveBackground { get; set; } = true;

    /// <summary>Áreas internas com a cor do fundo (miolo do "O", do "A"...) também viram tecido, sem ponto.</summary>
    public bool RemoveEnclosedBackground { get; set; } = true;

    /// <summary>Cores com diferença menor que isto (ΔE) são unidas no modo automático.</summary>
    public float MergeDeltaE { get; set; } = 16;

    /// <summary>
    /// Cores que cobrem menos que esta fração da imagem são descartadas no modo automático. Baixo de propósito:
    /// um logo com texto branco pequeno sobre fundo grande não pode perder o branco.
    /// </summary>
    public double MinColorShare { get; set; } = 0.0004;
}

/// <summary>Resultado da redução de cores: um rótulo por pixel (−1 = sem ponto: fundo/transparente).</summary>
public sealed class QuantizedImage
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int[] Labels { get; init; } = Array.Empty<int>();
    public List<(byte R, byte G, byte B)> Colors { get; init; } = new();
    /// <summary>Cor de fundo detectada (null se não houve).</summary>
    public (byte R, byte G, byte B)? Background { get; init; }
    /// <summary>true se havia pixels transparentes.</summary>
    public bool HasTransparency { get; init; }
}

public static class Quantizer
{
    private const float EdgeDeltaE = 7f;       // diferença entre vizinhos acima da qual o pixel é "de borda"
    private const float UnreliableDeltaE = 14f; // longe demais de qualquer cor: vira o rótulo do vizinho confiável mais próximo

    /// <param name="edgeRadius">
    /// Quantos pixels de distância contam para saber se um pixel é de borda. Em imagem ampliada, a faixa de
    /// suavização da borda fica larga (1 pixel original = vários pixels): o raio acompanha a ampliação.
    /// </param>
    public static QuantizedImage Run(RgbaImage image, QuantizeOptions o, CancellationToken ct = default, int edgeRadius = 1)
    {
        int w = image.Width, h = image.Height, n = w * h;
        var rgba = image.Data;

        // 1) Lab de cada pixel opaco.
        var lab = new Lab[n];
        var opaque = new bool[n];
        int transparent = 0;
        for (int i = 0; i < n; i++)
        {
            if (rgba[i * 4 + 3] < 128) { transparent++; continue; }
            opaque[i] = true;
            lab[i] = ColorSpace.ToLab(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]);
        }
        ct.ThrowIfCancellationRequested();

        // 2) Amostras para achar as cores: só pixels "planos" (sem borda), para o serrilhado e o ruído de JPEG não virarem cor.
        var flat = MarkFlat(lab, opaque, w, h, Math.Clamp(edgeRadius, 1, 8));
        int flatCount = flat.Count(f => f);
        // No modo automático usa TODOS os pixels: um traço fino não tem nenhum pixel "plano", e sua cor sumiria.
        // As cores de mistura das bordas (meio-tons entre duas cores) são descartadas depois, por PruneMixtures.
        bool useFlat = !o.Auto && flatCount >= Math.Max(200, n / 50);
        var samples = new List<Lab>();
        var sampleFlat = new List<bool>();
        int stride = Math.Max(1, (useFlat ? flatCount : n - transparent) / 60000);
        int k = 0;
        for (int i = 0; i < n; i++)
        {
            if (!opaque[i] || (useFlat && !flat[i])) continue;
            if (k++ % stride != 0) continue;
            samples.Add(lab[i]);
            sampleFlat.Add(flat[i]);
        }

        var result = new int[n];
        Array.Fill(result, -1);
        if (samples.Count == 0)
            return new QuantizedImage { Width = w, Height = h, Labels = result, HasTransparency = transparent > 0 };
        ct.ThrowIfCancellationRequested();

        // 3) k-means (com k-means++) e, no modo automático, limpeza das cores de mistura e união das parecidas.
        int kInit = o.Auto ? Math.Clamp(o.MaxColors + 4, 1, 28) : Math.Clamp(o.MaxColors, 1, 24);
        var centers = KMeans(samples, kInit, ct, out var counts);
        if (o.Auto)
        {
            counts = PruneMixtures(centers, counts, samples, sampleFlat);
            MergeCenters(centers, counts, samples.Count, o);
            CapColors(centers, o.MaxColors);
        }
        ct.ThrowIfCancellationRequested();

        // 4) Atribui todos os pixels à cor mais próxima; os longe demais de qualquer cor (bordas suavizadas) esperam.
        var dist = new float[n];
        var pending = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (!opaque[i]) continue;
            int best = 0;
            float bd = float.MaxValue;
            for (int c = 0; c < centers.Count; c++)
            {
                float d = Lab.DistSq(lab[i], centers[c]);
                if (d < bd) { bd = d; best = c; }
            }
            result[i] = best;
            dist[i] = MathF.Sqrt(bd);
        }
        var coreDist = (float[])dist.Clone(); // distância original à cor: só os pixels de "núcleo" definem a cor de cada camada

        // Fios finos: o pixel de um fio de 2 ou 3 pixels de espessura é uma MISTURA de cor e fundo, nunca a cor pura.
        // Sem tratamento ele iria para o vizinho mais próximo (o fundo) e o fio sumiria. Aqui, se o pixel tem
        // pelo menos 30% de uma cor sobre o fundo, ele conta como essa cor.
        if (o.RemoveBackground)
        {
            int bgEarly = FindBackgroundLabel(result, centers.Count, opaque, w, h);
            if (bgEarly >= 0) UnmixForeground(result, dist, rgba, opaque, centers, bgEarly);
        }
        ResolveUnreliable(result, dist, opaque, w, h, ct);

        // 5) Fundo e áreas internas do fundo.
        (byte, byte, byte)? bg = null;
        if (o.RemoveBackground) bg = RemoveBackground(result, centers, lab, opaque, w, h, o.RemoveEnclosedBackground);
        ct.ThrowIfCancellationRequested();

        return Finish(result, lab, coreDist, w, h, bg, transparent > 0);
    }

    // ---------- passos ----------

    private static bool[] MarkFlat(Lab[] lab, bool[] opaque, int w, int h, int radius)
    {
        var flat = new bool[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!opaque[i]) continue;
                bool ok = true;
                // Vizinhos a 1 pixel e a "radius" pixels, nas quatro direções.
                for (int d = 1; ok && d <= radius; d += Math.Max(1, radius - 1))
                {
                    if (x >= d && !Near(lab, opaque, i, i - d)) ok = false;
                    else if (x < w - d && !Near(lab, opaque, i, i + d)) ok = false;
                    else if (y >= d && !Near(lab, opaque, i, i - d * w)) ok = false;
                    else if (y < h - d && !Near(lab, opaque, i, i + d * w)) ok = false;
                }
                flat[i] = ok;
            }
        return flat;
    }

    private static bool Near(Lab[] lab, bool[] opaque, int i, int j) =>
        !opaque[j] || Lab.DeltaE(lab[i], lab[j]) <= EdgeDeltaE;

    /// <summary>Usado pelo detector de fotos: k-means simples, sem cancelamento.</summary>
    internal static List<Lab> KMeansForAnalysis(List<Lab> samples, int k) => KMeans(samples, k, CancellationToken.None, out _);

    private static List<Lab> KMeans(List<Lab> s, int k, CancellationToken ct, out int[] counts)
    {
        k = Math.Min(k, s.Count);
        var rng = new Random(12345); // fixo: mesma imagem, mesmo resultado
        var centers = new List<Lab> { s[rng.Next(s.Count)] };
        var d2 = new float[s.Count];
        Array.Fill(d2, float.MaxValue);

        while (centers.Count < k)
        {
            var last = centers[^1];
            double total = 0;
            for (int i = 0; i < s.Count; i++)
            {
                d2[i] = Math.Min(d2[i], Lab.DistSq(s[i], last));
                total += d2[i];
            }
            if (total <= 1e-6) break; // todas as amostras já coincidem com algum centro
            double pick = rng.NextDouble() * total, acc = 0;
            int chosen = s.Count - 1;
            for (int i = 0; i < s.Count; i++)
            {
                acc += d2[i];
                if (acc >= pick) { chosen = i; break; }
            }
            centers.Add(s[chosen]);
        }

        var assign = new int[s.Count];
        counts = new int[centers.Count];
        for (int iter = 0; iter < 25; iter++)
        {
            ct.ThrowIfCancellationRequested();
            bool changed = false;
            for (int i = 0; i < s.Count; i++)
            {
                int best = 0;
                float bd = float.MaxValue;
                for (int c = 0; c < centers.Count; c++)
                {
                    float d = Lab.DistSq(s[i], centers[c]);
                    if (d < bd) { bd = d; best = c; }
                }
                if (assign[i] != best || iter == 0) { assign[i] = best; changed = true; }
            }

            var sum = new double[centers.Count, 3];
            Array.Clear(counts);
            for (int i = 0; i < s.Count; i++)
            {
                int c = assign[i];
                counts[c]++;
                sum[c, 0] += s[i].L; sum[c, 1] += s[i].A; sum[c, 2] += s[i].B;
            }
            for (int c = 0; c < centers.Count; c++)
                if (counts[c] > 0)
                    centers[c] = new Lab((float)(sum[c, 0] / counts[c]), (float)(sum[c, 1] / counts[c]), (float)(sum[c, 2] / counts[c]));
            if (!changed) break;
        }
        return centers;
    }

    /// <summary>
    /// Remove as cores de "mistura": meio-tons que ficam entre duas outras cores (a faixa suavizada da borda, ou o meio
    /// de um degradê) e cujos pixels quase não são planos. Uma cor real entre duas outras (um cinza entre preto e branco)
    /// ocupa área, então a maioria dos seus pixels é plana e ela fica. Devolve o número de amostras de cada cor que restou.
    /// </summary>
    private static int[] PruneMixtures(List<Lab> centers, int[] counts, List<Lab> samples, List<bool> sampleFlat)
    {
        var cs = centers.ToList();
        int step = Math.Max(1, samples.Count / 20000);
        while (cs.Count > 2)
        {
            var total = new int[cs.Count];
            var flatN = new int[cs.Count];
            for (int i = 0; i < samples.Count; i += step)
            {
                int best = 0;
                float bd = float.MaxValue;
                for (int c = 0; c < cs.Count; c++)
                {
                    float d = Lab.DistSq(samples[i], cs[c]);
                    if (d < bd) { bd = d; best = c; }
                }
                total[best]++;
                if (sampleFlat[i]) flatN[best]++;
            }

            int worst = -1;
            double worstFraction = 1;
            for (int c = 0; c < cs.Count; c++)
            {
                double fraction = total[c] == 0 ? 0 : (double)flatN[c] / total[c];
                if (fraction >= 0.35 || !LiesBetweenOthers(cs, c)) continue;
                if (fraction < worstFraction) { worstFraction = fraction; worst = c; }
            }
            if (worst < 0) break;
            cs.RemoveAt(worst);
        }

        // Contagem de amostras de cada cor que ficou (para a união ponderada e o corte de cores raras).
        var kept = new int[cs.Count];
        for (int i = 0; i < samples.Count; i++)
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int c = 0; c < cs.Count; c++)
            {
                float d = Lab.DistSq(samples[i], cs[c]);
                if (d < bd) { bd = d; best = c; }
            }
            kept[best]++;
        }
        centers.Clear();
        centers.AddRange(cs);
        return kept;
    }

    /// <summary>A cor está (quase) sobre o segmento que liga duas outras cores, entre elas?</summary>
    private static bool LiesBetweenOthers(List<Lab> cs, int c)
    {
        // A suavização das bordas mistura as cores em RGB (não em Lab): o teste de "entre duas cores" é feito em RGB.
        var rgb = cs.Select(l => { var (r, g, b) = ColorSpace.ToRgb(l); return (R: (double)r, G: (double)g, B: (double)b); }).ToList();
        var p = rgb[c];
        for (int a = 0; a < rgb.Count; a++)
        {
            if (a == c) continue;
            for (int b = a + 1; b < rgb.Count; b++)
            {
                if (b == c) continue;
                double abR = rgb[b].R - rgb[a].R, abG = rgb[b].G - rgb[a].G, abB = rgb[b].B - rgb[a].B;
                double len2 = abR * abR + abG * abG + abB * abB;
                if (len2 < 40 * 40) continue; // extremos parecidos demais para haver "entre"
                double t = ((p.R - rgb[a].R) * abR + (p.G - rgb[a].G) * abG + (p.B - rgb[a].B) * abB) / len2;
                if (t < 0.05 || t > 0.95) continue;
                double dr = p.R - (rgb[a].R + t * abR), dg = p.G - (rgb[a].G + t * abG), db = p.B - (rgb[a].B + t * abB);
                if (Math.Sqrt(dr * dr + dg * dg + db * db) < 18) return true;
            }
        }
        return false;
    }

    /// <summary>No modo automático "até N cores": se sobrou mais, une os pares mais próximos.</summary>
    private static void CapColors(List<Lab> centers, int max)
    {
        while (centers.Count > Math.Max(1, max))
        {
            int bi = 0, bj = 1;
            float bd = float.MaxValue;
            for (int i = 0; i < centers.Count; i++)
                for (int j = i + 1; j < centers.Count; j++)
                {
                    float d = Lab.DeltaE(centers[i], centers[j]);
                    if (d < bd) { bd = d; bi = i; bj = j; }
                }
            centers[bi] = new Lab((centers[bi].L + centers[bj].L) / 2, (centers[bi].A + centers[bj].A) / 2, (centers[bi].B + centers[bj].B) / 2);
            centers.RemoveAt(bj);
        }
    }

    /// <summary>Une centros muito próximos (média ponderada) e descarta os que cobrem quase nada.</summary>
    private static void MergeCenters(List<Lab> centers, int[] counts, int sampleCount, QuantizeOptions o)
    {
        var cs = centers.ToList();
        var ns = counts.Select(c => (double)c).ToList();
        while (true)
        {
            int bi = -1, bj = -1;
            float bd = o.MergeDeltaE;
            for (int i = 0; i < cs.Count; i++)
                for (int j = i + 1; j < cs.Count; j++)
                {
                    float d = Lab.DeltaE(cs[i], cs[j]);
                    if (d < bd) { bd = d; bi = i; bj = j; }
                }
            if (bi < 0) break;
            double a = Math.Max(ns[bi], 1), b = Math.Max(ns[bj], 1), t = a + b;
            cs[bi] = new Lab((float)((cs[bi].L * a + cs[bj].L * b) / t), (float)((cs[bi].A * a + cs[bj].A * b) / t), (float)((cs[bi].B * a + cs[bj].B * b) / t));
            ns[bi] = ns[bi] + ns[bj];
            cs.RemoveAt(bj);
            ns.RemoveAt(bj);
        }

        // Cores raras: só some se sobrar pelo menos uma.
        for (int i = cs.Count - 1; i >= 0 && cs.Count > 1; i--)
            if (ns[i] / sampleCount < o.MinColorShare) { cs.RemoveAt(i); ns.RemoveAt(i); }

        centers.Clear();
        centers.AddRange(cs);
    }

    /// <summary>
    /// Pixels longe demais de qualquer cor (bordas suavizadas, ruído) recebem o rótulo do pixel confiável mais próximo,
    /// por busca em largura a partir de todos os pixels confiáveis ao mesmo tempo.
    /// </summary>
    private static void ResolveUnreliable(int[] labels, float[] dist, bool[] opaque, int w, int h, CancellationToken ct)
    {
        int n = w * h;
        var queue = new Queue<int>();
        var done = new bool[n];
        int reliable = 0;
        for (int i = 0; i < n; i++)
            if (opaque[i] && dist[i] <= UnreliableDeltaE) { done[i] = true; queue.Enqueue(i); reliable++; }
        if (reliable == 0) return; // imagem toda "incerta" (ex.: foto): mantém a cor mais próxima

        int guard = 0;
        while (queue.Count > 0)
        {
            if ((++guard & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            int i = queue.Dequeue();
            int x = i % w, y = i / w;
            if (x > 0) Visit(i - 1);
            if (x < w - 1) Visit(i + 1);
            if (y > 0) Visit(i - w);
            if (y < h - 1) Visit(i + w);

            void Visit(int j)
            {
                if (done[j] || !opaque[j]) return;
                done[j] = true;
                labels[j] = labels[i];
                queue.Enqueue(j);
            }
        }
    }

    /// <summary>Rótulo da cor que domina a faixa da borda da imagem (o fundo), ou −1 se nenhuma domina.</summary>
    private static int FindBackgroundLabel(int[] labels, int colorCount, bool[] opaque, int w, int h)
    {
        int band = Math.Max(2, Math.Min(w, h) / 40);
        var hist = new int[colorCount];
        int borderTotal = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (x >= band && x < w - band && y >= band && y < h - band) { x = w - band - 1; continue; }
                int i = y * w + x;
                borderTotal++;
                if (opaque[i]) hist[labels[i]]++;
            }
        int best = Array.IndexOf(hist, hist.Max());
        return hist[best] >= borderTotal * 0.5 ? best : -1;
    }

    /// <summary>
    /// Pixels "incertos" que são mistura de uma cor com o fundo (borda suavizada de um traço fino) passam a ser dessa cor
    /// quando têm 30% ou mais dela. Só vale para misturas de fato: o pixel tem de estar perto do segmento fundo→cor em RGB.
    /// </summary>
    private static void UnmixForeground(int[] labels, float[] dist, byte[] rgba, bool[] opaque, List<Lab> centers, int bg)
    {
        var rgb = centers.Select(l => { var (r, g, b) = ColorSpace.ToRgb(l); return (R: (double)r, G: (double)g, B: (double)b); }).ToList();
        var back = rgb[bg];
        for (int i = 0; i < labels.Length; i++)
        {
            if (!opaque[i] || dist[i] <= UnreliableDeltaE) continue;
            double pr = rgba[i * 4] - back.R, pg = rgba[i * 4 + 1] - back.G, pb = rgba[i * 4 + 2] - back.B;
            int best = -1;
            double bestPerp = 30;
            for (int c = 0; c < rgb.Count; c++)
            {
                if (c == bg) continue;
                double ar = rgb[c].R - back.R, ag = rgb[c].G - back.G, ab = rgb[c].B - back.B;
                double len2 = ar * ar + ag * ag + ab * ab;
                if (len2 < 60 * 60) continue;                       // cor parecida demais com o fundo
                double t = (pr * ar + pg * ag + pb * ab) / len2;    // quanto da cor há no pixel (0 = fundo, 1 = cor pura)
                if (t < 0.30 || t > 1.15) continue;
                double er = pr - t * ar, eg = pg - t * ag, eb = pb - t * ab;
                double perp = Math.Sqrt(er * er + eg * eg + eb * eb); // distância ao segmento: é mesmo uma mistura?
                if (perp < bestPerp) { bestPerp = perp; best = c; }
            }
            if (best >= 0) { labels[i] = best; dist[i] = 0; }
        }
    }

    /// <summary>
    /// Fundo = cor que domina a faixa da borda da imagem. Sempre remove a parte ligada à borda;
    /// opcionalmente remove também as áreas internas dessa mesma cor.
    /// </summary>
    private static (byte, byte, byte)? RemoveBackground(int[] labels, List<Lab> centers, Lab[] lab, bool[] opaque, int w, int h, bool enclosed)
    {
        int band = Math.Max(2, Math.Min(w, h) / 40);
        var hist = new int[centers.Count];
        int borderTotal = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (x >= band && x < w - band && y >= band && y < h - band) { x = w - band - 1; continue; }
                int i = y * w + x;
                borderTotal++;
                if (opaque[i]) hist[labels[i]]++;
            }
        int bgLabel = Array.IndexOf(hist, hist.Max());
        // Transparente também conta como fundo; a cor só é fundo se dominar a borda entre os pixels opacos e ocupar boa parte dela.
        if (hist[bgLabel] < borderTotal * 0.5) return null;

        var visited = new bool[w * h];
        var queue = new Queue<int>();
        void Seed(int i) { if (!visited[i] && opaque[i] && labels[i] == bgLabel) { visited[i] = true; queue.Enqueue(i); } }
        for (int x = 0; x < w; x++) { Seed(x); Seed((h - 1) * w + x); }
        for (int y = 0; y < h; y++) { Seed(y * w); Seed(y * w + w - 1); }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            labels[i] = -1;
            int x = i % w, y = i / w;
            if (x > 0) Seed(i - 1);
            if (x < w - 1) Seed(i + 1);
            if (y > 0) Seed(i - w);
            if (y < h - 1) Seed(i + w);
        }
        if (enclosed)
            for (int i = 0; i < labels.Length; i++)
                if (opaque[i] && labels[i] == bgLabel) labels[i] = -1;

        var (r, g, b) = ColorSpace.ToRgb(centers[bgLabel]);
        return (r, g, b);
    }

    /// <summary>Renumera as cores que sobraram e calcula a cor de cada uma pela média dos pixels (já sem as bordas incertas).</summary>
    private static QuantizedImage Finish(int[] labels, Lab[] lab, float[] coreDist, int w, int h, (byte, byte, byte)? bg, bool hasTransparency)
    {
        var remap = new Dictionary<int, int>();
        var core = new List<(double L, double A, double B, long N)>();  // só pixels de núcleo (cor confiável)
        var all = new List<(double L, double A, double B, long N)>();   // todos (usado se a camada for só borda, como um fio fino)
        for (int i = 0; i < labels.Length; i++)
        {
            int l = labels[i];
            if (l < 0) continue;
            if (!remap.TryGetValue(l, out var m)) { m = remap.Count; remap[l] = m; core.Add((0, 0, 0, 0)); all.Add((0, 0, 0, 0)); }
            labels[i] = m;
            var a = all[m];
            all[m] = (a.L + lab[i].L, a.A + lab[i].A, a.B + lab[i].B, a.N + 1);
            if (coreDist[i] <= UnreliableDeltaE)
            {
                var s = core[m];
                core[m] = (s.L + lab[i].L, s.A + lab[i].A, s.B + lab[i].B, s.N + 1);
            }
        }
        // A cor da camada vem dos pixels de núcleo: as bordas misturadas com o fundo clareariam/escureceriam a média.
        var colors = Enumerable.Range(0, core.Count).Select(m =>
        {
            var s = core[m].N > 0 ? core[m] : all[m];
            return ColorSpace.ToRgb(new Lab((float)(s.L / s.N), (float)(s.A / s.N), (float)(s.B / s.N)));
        }).ToList();
        return new QuantizedImage { Width = w, Height = h, Labels = labels, Colors = colors, Background = bg, HasTransparency = hasTransparency };
    }
}
