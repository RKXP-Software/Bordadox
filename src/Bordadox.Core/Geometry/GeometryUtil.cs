namespace Bordadox.Core.Geometry;

public readonly record struct Rect2(double MinX, double MinY, double MaxX, double MaxY)
{
    public static readonly Rect2 Empty = new(double.PositiveInfinity, double.PositiveInfinity, double.NegativeInfinity, double.NegativeInfinity);

    public bool IsEmpty => MinX > MaxX || MinY > MaxY;
    public double Width => IsEmpty ? 0 : MaxX - MinX;
    public double Height => IsEmpty ? 0 : MaxY - MinY;
    public Vec2 Center => new((MinX + MaxX) / 2, (MinY + MaxY) / 2);

    public Rect2 Include(Vec2 p) => new(Math.Min(MinX, p.X), Math.Min(MinY, p.Y), Math.Max(MaxX, p.X), Math.Max(MaxY, p.Y));

    public Rect2 Union(Rect2 o) => o.IsEmpty ? this : IsEmpty ? o :
        new(Math.Min(MinX, o.MinX), Math.Min(MinY, o.MinY), Math.Max(MaxX, o.MaxX), Math.Max(MaxY, o.MaxY));

    public Rect2 Inflate(double d) => IsEmpty ? this : new(MinX - d, MinY - d, MaxX + d, MaxY + d);

    public bool Contains(Vec2 p) => p.X >= MinX && p.X <= MaxX && p.Y >= MinY && p.Y <= MaxY;

    public static Rect2 FromPoints(IEnumerable<Vec2> pts)
    {
        var r = Empty;
        foreach (var p in pts) r = r.Include(p);
        return r;
    }
}

public static class GeometryUtil
{
    /// <summary>Divide uma polilinha em pontos com espaçamento máximo <paramref name="step"/>, preservando os cantos.</summary>
    public static List<Vec2> ResamplePreservingCorners(IReadOnlyList<Vec2> pts, double step)
    {
        var result = new List<Vec2>();
        if (pts.Count == 0) return result;
        result.Add(pts[0]);
        for (int i = 1; i < pts.Count; i++)
        {
            var a = pts[i - 1];
            var b = pts[i];
            var len = Vec2.Distance(a, b);
            if (len < 1e-6) continue;
            int n = Math.Max(1, (int)Math.Ceiling(len / step - 1e-9));
            for (int k = 1; k <= n; k++) result.Add(Vec2.Lerp(a, b, (double)k / n));
        }
        return result;
    }

    /// <summary>Amostra uma polilinha em intervalos uniformes ao longo do comprimento total (não preserva cantos).</summary>
    public static List<(Vec2 Point, Vec2 Tangent)> SampleUniform(IReadOnlyList<Vec2> pts, double step)
    {
        var result = new List<(Vec2, Vec2)>();
        if (pts.Count < 2) return result;

        var cum = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + Vec2.Distance(pts[i - 1], pts[i]);
        var total = cum[^1];
        if (total < 1e-6) return result;

        int n = Math.Max(1, (int)Math.Round(total / step));
        int seg = 1;
        for (int k = 0; k <= n; k++)
        {
            var d = total * k / n;
            while (seg < pts.Count - 1 && cum[seg] < d) seg++;
            var a = pts[seg - 1];
            var b = pts[seg];
            var segLen = cum[seg] - cum[seg - 1];
            var t = segLen < 1e-9 ? 0 : (d - cum[seg - 1]) / segLen;
            var p = Vec2.Lerp(a, b, Math.Clamp(t, 0, 1));
            result.Add((p, TangentAt(pts, seg, t)));
        }
        return result;
    }

    // Tangente suavizada: nos vértices usa a média das duas arestas (bissetriz) para evitar cruzamentos.
    private static Vec2 TangentAt(IReadOnlyList<Vec2> pts, int seg, double t)
    {
        var cur = (pts[seg] - pts[seg - 1]).Normalized();
        if (t > 0.999 && seg + 1 < pts.Count)
        {
            var next = (pts[seg + 1] - pts[seg]).Normalized();
            var avg = (cur + next).Normalized();
            return avg == Vec2.Zero ? cur : avg;
        }
        if (t < 0.001 && seg - 2 >= 0)
        {
            var prev = (pts[seg - 1] - pts[seg - 2]).Normalized();
            var avg = (prev + cur).Normalized();
            return avg == Vec2.Zero ? cur : avg;
        }
        return cur;
    }

    /// <summary>
    /// Direção em que um polígono é mais comprido (graus, −90..90), por análise de componentes principais do contorno.
    /// Usada para fazer os pontos do satin cruzarem o lado estreito da forma.
    /// </summary>
    public static double MainAxisDegrees(IReadOnlyList<Vec2> polygon)
    {
        double sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0, n = 0;
        for (int i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
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

    public static double PolylineLength(IReadOnlyList<Vec2> pts)
    {
        double total = 0;
        for (int i = 1; i < pts.Count; i++) total += Vec2.Distance(pts[i - 1], pts[i]);
        return total;
    }

    public static double DistanceToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared;
        if (len2 < 1e-12) return Vec2.Distance(p, a);
        var t = Math.Clamp(Vec2.Dot(p - a, ab) / len2, 0, 1);
        return Vec2.Distance(p, a + ab * t);
    }

    public static double DistanceToPolyline(Vec2 p, IReadOnlyList<Vec2> pts, bool closed)
    {
        if (pts.Count == 0) return double.PositiveInfinity;
        if (pts.Count == 1) return Vec2.Distance(p, pts[0]);
        double best = double.PositiveInfinity;
        for (int i = 1; i < pts.Count; i++) best = Math.Min(best, DistanceToSegment(p, pts[i - 1], pts[i]));
        if (closed) best = Math.Min(best, DistanceToSegment(p, pts[^1], pts[0]));
        return best;
    }

    /// <summary>Teste par-ímpar de ponto em um conjunto de contornos (contorno externo + furos).</summary>
    public static bool PointInContours(Vec2 p, IEnumerable<IReadOnlyList<Vec2>> contours)
    {
        bool inside = false;
        foreach (var c in contours)
        {
            for (int i = 0, j = c.Count - 1; i < c.Count; j = i++)
            {
                var a = c[i];
                var b = c[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
        }
        return inside;
    }

    public static double SignedArea(IReadOnlyList<Vec2> c)
    {
        double a = 0;
        for (int i = 0, j = c.Count - 1; i < c.Count; j = i++) a += Vec2.Cross(c[j], c[i]);
        return a / 2;
    }

    /// <summary>Remove pontos quase colineares/duplicados (Douglas-Peucker).</summary>
    public static List<Vec2> Simplify(IReadOnlyList<Vec2> pts, double tolerance)
    {
        if (pts.Count < 3) return pts.ToList();
        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            var (s, e) = stack.Pop();
            double maxD = 0;
            int idx = -1;
            for (int i = s + 1; i < e; i++)
            {
                var d = DistanceToSegment(pts[i], pts[s], pts[e]);
                if (d > maxD) { maxD = d; idx = i; }
            }
            if (idx >= 0 && maxD > tolerance)
            {
                keep[idx] = true;
                stack.Push((s, idx));
                stack.Push((idx, e));
            }
        }
        var result = new List<Vec2>();
        for (int i = 0; i < pts.Count; i++) if (keep[i]) result.Add(pts[i]);
        return result;
    }
}
