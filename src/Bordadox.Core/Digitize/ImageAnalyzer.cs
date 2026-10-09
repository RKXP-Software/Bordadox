namespace Bordadox.Core.Digitize;

public enum ImageKind
{
    /// <summary>Poucas cores chapadas (logotipo, ícone, desenho): ideal para digitalizar.</summary>
    Flat,
    /// <summary>Degradês ou muitas cores: vai virar faixas de poucas cores.</summary>
    Gradient,
    /// <summary>Fotografia ou imagem com textura: o resultado costuma ser ruim e pesado.</summary>
    Photo,
}

public sealed class ImageAnalysis
{
    public ImageKind Kind { get; init; }
    /// <summary>Cores significativas (cada uma com pelo menos 0,4% da imagem).</summary>
    public int DistinctColors { get; init; }
    /// <summary>Fração dos pixels com variação suave contínua (textura/ruído), típica de foto.</summary>
    public double TextureFraction { get; init; }
    /// <summary>Erro médio (ΔE) ao reduzir a 8 cores: baixo em logos, alto em fotos.</summary>
    public double QuantizationError { get; init; }
    public double Megapixels { get; init; }
    public bool HasTransparency { get; init; }

    /// <summary>
    /// Se o usuário deve confirmar os riscos antes de continuar: fotos, e degradês fortes
    /// (que viram muitas faixas de cor, com resultado ruim e pesado).
    /// </summary>
    public bool NeedsConfirmation => Kind == ImageKind.Photo || (Kind == ImageKind.Gradient && QuantizationError >= 9);
}

public static class ImageAnalyzer
{
    private const int AnalysisSide = 256;
    private const double TextureBand = 1.6; // ΔE entre vizinhos: acima disso e abaixo de EdgeDeltaE conta como textura
    private const double EdgeDeltaE = 8;

    public static ImageAnalysis Analyze(RgbaImage source)
    {
        var img = source.Limit(AnalysisSide);
        int w = img.Width, h = img.Height, n = w * h;

        var lab = new Lab[n];
        var opaque = new bool[n];
        var bins = new Dictionary<int, int>();
        int opaqueCount = 0, transparent = 0;
        for (int i = 0; i < n; i++)
        {
            byte r = img.Data[i * 4], g = img.Data[i * 4 + 1], b = img.Data[i * 4 + 2];
            if (img.Data[i * 4 + 3] < 128) { transparent++; continue; }
            opaque[i] = true;
            opaqueCount++;
            lab[i] = ColorSpace.ToLab(r, g, b);
            int key = (r >> 4) << 8 | (g >> 4) << 4 | (b >> 4);
            bins[key] = bins.GetValueOrDefault(key) + 1;
        }

        double megapixels = source.Width * (double)source.Height / 1e6;
        if (opaqueCount == 0)
            return new ImageAnalysis { Kind = ImageKind.Flat, Megapixels = megapixels, HasTransparency = transparent > 0 };

        int distinct = bins.Count(kv => kv.Value >= opaqueCount * 0.004);

        // Textura: vizinhos que variam um pouco (nem iguais, nem uma borda nítida).
        int textured = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!opaque[i]) continue;
                double m = 0;
                if (x < w - 1 && opaque[i + 1]) m = Math.Max(m, Lab.DeltaE(lab[i], lab[i + 1]));
                if (y < h - 1 && opaque[i + w]) m = Math.Max(m, Lab.DeltaE(lab[i], lab[i + w]));
                if (m > TextureBand && m < EdgeDeltaE) textured++;
            }
        double texture = (double)textured / opaqueCount;

        // Erro de reduzir a 8 cores.
        var samples = new List<Lab>();
        int stride = Math.Max(1, opaqueCount / 20000), k = 0;
        for (int i = 0; i < n; i++)
            if (opaque[i] && k++ % stride == 0) samples.Add(lab[i]);
        var centers = Quantizer.KMeansForAnalysis(samples, 8);
        double err = 0;
        foreach (var s in samples)
        {
            float best = float.MaxValue;
            foreach (var c in centers) best = Math.Min(best, Lab.DistSq(s, c));
            err += Math.Sqrt(best);
        }
        err /= samples.Count;

        // Foto = textura por toda parte (ou muita cor com alguma textura). Degradê liso não tem textura: vira "degradê".
        ImageKind kind;
        if (texture >= 0.30 || (err >= 9 && distinct >= 40 && texture >= 0.12)) kind = ImageKind.Photo;
        else if (distinct >= 24 || err >= 5.5) kind = ImageKind.Gradient;
        else kind = ImageKind.Flat;

        return new ImageAnalysis
        {
            Kind = kind,
            DistinctColors = distinct,
            TextureFraction = texture,
            QuantizationError = err,
            Megapixels = megapixels,
            HasTransparency = transparent > 0,
        };
    }
}
