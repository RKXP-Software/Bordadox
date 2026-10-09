namespace Bordadox.Core.Digitize;

/// <summary>
/// Une os "tons" de um mesmo degradê (por exemplo as várias tonalidades de um dourado metálico) numa só cor.
/// Dois tons só se unem se forem da mesma família de cor, estiverem encostados e a transição entre eles na imagem
/// original for SUAVE (vizinhos dos dois lados da divisa quase iguais). Duas cores chapadas diferentes têm uma borda
/// seca, com salto de cor, e por isso não se unem.
/// </summary>
public static class ShadeMerger
{
    /// <summary>Tons com cor média mais distante que isto (ΔE) nunca se unem, mesmo com transição suave.</summary>
    public const float MaxShadeDeltaE = 45f;

    private const float NeutralChroma = 12f;     // abaixo disto a cor é um cinza: não tem matiz
    private const double MaxHueDiffDeg = 30;
    private const double SoftFraction = 0.30;    // salto médio na divisa ≤ 30% da diferença entre os tons
    private const int MinContact = 12;           // pares de pixels encostados para considerar vizinhos

    public static QuantizedImage Merge(QuantizedImage q, RgbaImage image, int edgeRadius = 1)
    {
        int n = q.Colors.Count;
        if (n < 2) return q;
        int w = q.Width, h = q.Height;
        var labels = q.Labels;

        var centers = q.Colors.Select(c => ColorSpace.ToLab(c.R, c.G, c.B)).ToArray();
        var area = new long[n];
        var contact = new Dictionary<int, List<float>>();

        Lab PixelLab(int i) => ColorSpace.ToLab(image.Data[i * 4], image.Data[i * 4 + 1], image.Data[i * 4 + 2]);

        void Touch(int i, int j)
        {
            int a = labels[i], b = labels[j];
            if (b < 0 || a == b) return;
            int key = Math.Min(a, b) * n + Math.Max(a, b);
            if (!contact.TryGetValue(key, out var jumps)) contact[key] = jumps = new List<float>();
            jumps.Add(Lab.DeltaE(PixelLab(i), PixelLab(j)));
        }

        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (labels[i] < 0) continue;
                area[labels[i]]++;
                if (x + 1 < w) Touch(i, i + 1);
                if (y + 1 < h) Touch(i, i + w);
            }

        var parent = Enumerable.Range(0, n).ToArray();
        int Find(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }
        bool merged = false;

        foreach (var (key, jumps) in contact)
        {
            int a = key / n, b = key % n;
            if (jumps.Count < MinContact) continue;
            float de = Lab.DeltaE(centers[a], centers[b]);
            if (de > MaxShadeDeltaE || de < 1) { if (de < 1) { parent[Find(a)] = Find(b); merged = true; } continue; }
            if (!SameFamily(centers[a], centers[b])) continue;
            // Mediana, não média: pixels de borda borrada (mistura com o fundo) têm saltos enormes e não dizem nada sobre o degradê.
            jumps.Sort();
            double medianJump = jumps[jumps.Count / 2];
            if (medianJump > SoftFraction * de / Math.Max(1, edgeRadius)) continue;
            parent[Find(a)] = Find(b);
            merged = true;
        }
        if (!merged) return q;

        // Cor de cada grupo: média (ponderada pela área) em Lab. Novos índices na ordem em que aparecem.
        var groupOf = new Dictionary<int, int>();
        var remap = new int[n];
        var sum = new List<(double L, double A, double B, double N)>();
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            if (!groupOf.TryGetValue(root, out int g)) { g = groupOf.Count; groupOf[root] = g; sum.Add((0, 0, 0, 0)); }
            remap[i] = g;
            double wgt = Math.Max(1, area[i]);
            var s = sum[g];
            sum[g] = (s.L + centers[i].L * wgt, s.A + centers[i].A * wgt, s.B + centers[i].B * wgt, s.N + wgt);
        }
        var colors = sum.Select(s => ColorSpace.ToRgb(new Lab((float)(s.L / s.N), (float)(s.A / s.N), (float)(s.B / s.N)))).ToList();
        var newLabels = new int[labels.Length];
        for (int i = 0; i < labels.Length; i++) newLabels[i] = labels[i] < 0 ? -1 : remap[labels[i]];

        return new QuantizedImage
        {
            Width = w, Height = h, Labels = newLabels, Colors = colors, Background = q.Background, HasTransparency = q.HasTransparency,
        };
    }

    private static bool SameFamily(in Lab a, in Lab b)
    {
        double ca = Math.Sqrt(a.A * a.A + a.B * a.B), cb = Math.Sqrt(b.A * b.A + b.B * b.B);
        bool na = ca < NeutralChroma, nb = cb < NeutralChroma;
        if (na && nb) return true;      // dois cinzas (prata metálico)
        if (na || nb) return false;     // um cinza e um colorido são cores diferentes
        double ha = Math.Atan2(a.B, a.A) * 180 / Math.PI, hb = Math.Atan2(b.B, b.A) * 180 / Math.PI;
        double d = Math.Abs(ha - hb) % 360;
        if (d > 180) d = 360 - d;
        return d <= MaxHueDiffDeg;
    }
}
