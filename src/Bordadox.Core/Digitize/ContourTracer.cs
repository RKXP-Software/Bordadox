namespace Bordadox.Core.Digitize;

public readonly record struct PointI(int X, int Y);

/// <summary>Uma região conexa de uma máscara: contorno externo e furos, nas coordenadas (cantos de pixel) da imagem.</summary>
public sealed class Shape
{
    public List<PointI> Outer { get; init; } = new();
    public List<List<PointI>> Holes { get; init; } = new();
    /// <summary>Pixels da região (sem os furos).</summary>
    public int Area { get; init; }
}

/// <summary>
/// Acompanha as bordas dos pixels de uma máscara e monta os contornos exatos (escada de pixels).
/// Regiões na diagonal NÃO se ligam (vizinhança de 4), então cada região tem um único contorno externo.
/// </summary>
public static class ContourTracer
{
    // Direções: 0 = leste, 1 = sul, 2 = oeste, 3 = norte (Y para baixo). "Virar à direita" = (d + 1) % 4.
    private static readonly int[] Dx = { 1, 0, -1, 0 };
    private static readonly int[] Dy = { 0, 1, 0, -1 };

    public static List<Shape> Trace(bool[] mask, int w, int h, CancellationToken ct = default)
    {
        // Região de cada pixel (só da máscara).
        var labels = new int[w * h];
        for (int i = 0; i < labels.Length; i++) labels[i] = mask[i] ? 1 : 0;
        var comps = ComponentMap.Build(labels, w, h, ct);

        // Arestas de borda, orientadas com o interior à direita. Guardadas por canto e direção de saída.
        int cw = w + 1;
        var edges = new byte[cw * (h + 1)];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (!mask[y * w + x]) continue;
                if (y == 0 || !mask[(y - 1) * w + x]) edges[y * cw + x] |= 1 << 0;            // topo: (x,y) → leste
                if (x == w - 1 || !mask[y * w + x + 1]) edges[y * cw + x + 1] |= 1 << 1;      // direita: (x+1,y) → sul
                if (y == h - 1 || !mask[(y + 1) * w + x]) edges[(y + 1) * cw + x + 1] |= 1 << 2; // base: (x+1,y+1) → oeste
                if (x == 0 || !mask[y * w + x - 1]) edges[(y + 1) * cw + x] |= 1 << 3;        // esquerda: (x,y+1) → norte
            }

        var visited = new byte[edges.Length];
        var shapes = new Dictionary<int, (List<PointI> Outer, List<List<PointI>> Holes)>();

        for (int c = 0; c < edges.Length; c++)
        {
            if (edges[c] == 0) continue;
            if ((c & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            for (int d0 = 0; d0 < 4; d0++)
            {
                int bit = 1 << d0;
                if ((edges[c] & bit) == 0 || (visited[c] & bit) != 0) continue;

                var loop = Follow(edges, visited, cw, c, d0, out double signedArea);

                // Pixel à direita da primeira aresta pertence à região dona do contorno.
                int cx = c % cw, cy = c / cw;
                var (px, py) = d0 switch
                {
                    0 => (cx, cy),
                    1 => (cx - 1, cy),
                    2 => (cx - 1, cy - 1),
                    _ => (cx, cy - 1),
                };
                int comp = comps.Id[py * w + px];
                if (!shapes.TryGetValue(comp, out var s)) shapes[comp] = s = (new List<PointI>(), new List<List<PointI>>());
                if (signedArea > 0) shapes[comp] = (loop, s.Holes);
                else s.Holes.Add(loop);
            }
        }

        var result = new List<Shape>();
        foreach (var (comp, s) in shapes.OrderBy(kv => kv.Key))
        {
            if (s.Outer.Count == 0) continue;
            result.Add(new Shape { Outer = s.Outer, Holes = s.Holes, Area = comps.Items[comp].Area });
        }
        return result;
    }

    private static List<PointI> Follow(byte[] edges, byte[] visited, int cw, int startCorner, int startDir, out double signedArea)
    {
        var pts = new List<PointI>();
        int c = startCorner, d = startDir;
        double area2 = 0;

        while (true)
        {
            visited[c] |= (byte)(1 << d);
            int x = c % cw, y = c / cw;
            int nx = x + Dx[d], ny = y + Dy[d];
            int nc = ny * cw + nx;

            // Próxima direção: direita, em frente, esquerda (nessa ordem) — mantém regiões diagonais separadas.
            int next = -1;
            foreach (int cand in new[] { (d + 1) % 4, d, (d + 3) % 4 })
                if ((edges[nc] & (1 << cand)) != 0) { next = cand; break; }
            if (next < 0) throw new InvalidOperationException("Contorno aberto: borda de pixel sem continuação.");

            area2 += (double)x * ny - (double)nx * y;
            if (next != d) pts.Add(new PointI(nx, ny)); // só os cantos
            c = nc;
            d = next;
            if (c == startCorner && d == startDir) break;
        }

        signedArea = area2 / 2;
        return pts;
    }
}
