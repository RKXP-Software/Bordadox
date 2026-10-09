using Bordadox.Core.Digitize;
using Bordadox.Core.Geometry;

namespace Bordadox.Core.Text;

/// <summary>
/// Satin em colunas para letras: em vez de linhas paralelas de um ângulo só, os pontos cruzam o traço de lado a lado,
/// perpendiculares à linha central, como numa coluna satin digitalizada à mão. A linha central vem do esqueleto da letra
/// e a largura de cada ponto vem de um raio lançado perpendicularmente até os dois lados do contorno.
/// Cruzamentos (T, X, E...) viram colunas que se sobrepõem; o que não se presta a colunas cai para o preenchimento comum.
/// </summary>
public static class ColumnSatin
{
    private const double PixelsPerMm = 20;

    /// <summary>
    /// Colunas (pares de pontos nas duas bordas, na ordem ao longo da coluna) que cobrem uma forma conexa
    /// (contorno externo + furos, em mm). Devolve null se a forma não se presta a colunas (grossa demais,
    /// ou a cobertura não fecha com a área da forma).
    /// </summary>
    public static List<List<(Vec2 A, Vec2 B)>>? Build(IReadOnlyList<IReadOnlyList<Vec2>> contours, double pairSpacing, double maxWidthMm)
    {
        if (contours.Count == 0) return null;
        var box = Rect2.FromPoints(contours.SelectMany(c => c)).Inflate(1.0);
        double cell = 1.0 / PixelsPerMm;
        int w = (int)Math.Ceiling(box.Width / cell), h = (int)Math.Ceiling(box.Height / cell);
        if (w < 8 || h < 8 || w * (long)h > 4_000_000) return null;

        var mask = PolygonRaster.Fill(contours, box.MinX, box.MinY, cell, w, h);
        double area = mask.Count(m => m) * cell * cell;
        double perimeter = contours.Sum(c => GeometryUtil.PolylineLength(c.Append(c[0]).ToList()));
        if (area < 0.2 || perimeter < 1e-6) return null;
        double typicalWidth = 2 * area / perimeter;
        if (typicalWidth > maxWidthMm) return null;

        var skeleton = Skeleton.Thin(mask, w, h);
        Skeleton.Prune(skeleton, w, h, (int)Math.Ceiling((0.75 * typicalWidth + 0.3) * PixelsPerMm));
        int cluster = Math.Max(3, (int)Math.Round(typicalWidth * PixelsPerMm / 2));
        var paths = Skeleton.JoinAtJunctions(Skeleton.Trace(skeleton, w, h), skeleton, w, h, cluster, 2 * cluster + 2)
            .Where(p => p.Count >= 4).ToList();
        if (paths.Count == 0) return null;

        var segments = contours.Where(c => c.Count >= 3)
            .SelectMany(c => Enumerable.Range(0, c.Count).Select(i => (A: c[i], B: c[(i + 1) % c.Count]))).ToList();

        var columns = new List<List<(Vec2 A, Vec2 B)>>();
        double covered = 0;
        foreach (var path in paths)
        {
            bool JunctionAt(PointI p) => Skeleton.Branches(skeleton, w, h, p.X, p.Y) >= 3;
            foreach (var pairs in ColumnsFor(path, JunctionAt(path[0]), JunctionAt(path[^1]), box, cell, segments, pairSpacing))
            {
                columns.Add(pairs);
                for (int i = 1; i < pairs.Count; i++)
                {
                    var m0 = Vec2.Lerp(pairs[i - 1].A, pairs[i - 1].B, 0.5);
                    var m1 = Vec2.Lerp(pairs[i].A, pairs[i].B, 0.5);
                    covered += Vec2.Distance(m0, m1) * (Vec2.Distance(pairs[i].A, pairs[i].B) + Vec2.Distance(pairs[i - 1].A, pairs[i - 1].B)) / 2;
                }
            }
        }
        if (columns.Count == 0) return null;
        double ratio = covered / area;
        return ratio is >= 0.75 and <= 1.8 ? columns : null;
    }

    private static IEnumerable<List<(Vec2 A, Vec2 B)>> ColumnsFor(List<PointI> path, bool junctionAtStart, bool junctionAtEnd, Rect2 box, double cell,
        List<(Vec2 A, Vec2 B)> segments, double step)
    {
        bool closed = path.Count > 6 && path[0] == path[^1];
        var raw = path.Select(p => new Vec2(box.MinX + (p.X + 0.5) * cell, box.MinY + (p.Y + 0.5) * cell)).ToList();
        if (closed) raw.RemoveAt(raw.Count - 1);

        var pts = Resample(raw, step, closed);
        if (pts.Count < 3) yield break;
        pts = Smooth(pts, Math.Max(1, (int)Math.Round(0.4 / step)), closed);

        // Quinas vivas (A, V, N, Z, L, 7...): cada lado vira uma coluna própria, com pontos paralelos ao seu traço,
        // em vez de um leque em volta da quina. As colunas se sobrepõem na quina.
        var pieces = closed ? new List<(List<Vec2>, bool, bool)> { (pts, false, false) } : SplitAtCorners(pts, junctionAtStart, junctionAtEnd, step);
        foreach (var (piece, jStart, jEnd) in pieces)
        {
            var pairs = ColumnForPiece(piece, jStart, jEnd, closed, segments, step);
            if (pairs != null) yield return pairs;
        }
    }

    private static List<(List<Vec2> Pts, bool JStart, bool JEnd)> SplitAtCorners(List<Vec2> pts, bool jStart, bool jEnd, double step)
    {
        int n = pts.Count;
        int k = Math.Max(2, (int)Math.Round(0.8 / step));
        int minPiece = Math.Max(6, (int)Math.Round(1.5 / step));
        var cuts = new List<int>();
        if (n >= 2 * minPiece)
        {
            var angle = new double[n];
            for (int i = k; i < n - k; i++)
            {
                var d1 = (pts[i] - pts[i - k]).Normalized();
                var d2 = (pts[i + k] - pts[i]).Normalized();
                angle[i] = Math.Acos(Math.Clamp(Vec2.Dot(d1, d2), -1, 1)) * 180 / Math.PI;
            }
            for (int i = minPiece; i < n - minPiece; i++)
            {
                if (angle[i] < 55) continue;
                bool peak = true;
                for (int j = Math.Max(k, i - k); j <= Math.Min(n - 1 - k, i + k) && peak; j++) if (angle[j] > angle[i]) peak = false;
                if (peak && (cuts.Count == 0 || i - cuts[^1] >= minPiece)) cuts.Add(i);
            }
        }
        var result = new List<(List<Vec2>, bool, bool)>();
        int start = 0;
        for (int c = 0; c <= cuts.Count; c++)
        {
            int end = c < cuts.Count ? cuts[c] : n - 1;
            result.Add((pts.GetRange(start, end - start + 1), start == 0 ? jStart : true, c == cuts.Count ? jEnd : true));
            start = end;
        }
        return result;
    }

    private static List<(Vec2 A, Vec2 B)>? ColumnForPiece(List<Vec2> pts, bool junctionAtStart, bool junctionAtEnd, bool closed,
        List<(Vec2 A, Vec2 B)> segments, double step)
    {

        // Largura típica DESTE trecho (mediana): onde o raio cruza outro traço ele dá um valor enorme, que é descartado.
        double typical = MedianWidth(pts, segments, closed);
        if (double.IsNaN(typical) || typical < 0.15) return null;

        if (!closed)
        {
            // Perto de uma bifurcação o esqueleto faz uma curva para a quina (o que abriria um leque de pontos): corta esse pedaço
            // e prolonga reto, na direção do traço, até o contorno. Nas pontas livres só prolonga.
            // (a ponta livre também encurva um pouco em direção à quina da ponta, por isso corta menos)
            pts = TrimEnd(pts, (junctionAtStart ? 1.1 : 0.6) * typical, step, atStart: true);
            pts = TrimEnd(pts, (junctionAtEnd ? 1.1 : 0.6) * typical, step, atStart: false);
            if (pts.Count < 3) return null;
            pts = ExtendEnd(pts, segments, step, atStart: true);
            pts = ExtendEnd(pts, segments, step, atStart: false);
        }

        int n = pts.Count;
        var pairs = new List<(Vec2 A, Vec2 B)>(n);
        double mh = typical / 2;
        for (int i = 0; i < n; i++)
        {
            var prev = pts[closed ? (i + n - 1) % n : Math.Max(0, i - 1)];
            var next = pts[closed ? (i + 1) % n : Math.Min(n - 1, i + 1)];
            var normal = (next - prev).Normalized().Perp();
            double a = Ray(pts[i], normal, segments), b = Ray(pts[i], -normal, segments);

            // Largura de um lado muito acima da típica = o raio entrou em outro traço (cruzamento): usa o outro lado.
            bool aBig = double.IsNaN(a) || a > 1.5 * mh, bBig = double.IsNaN(b) || b > 1.5 * mh;
            if (aBig && bBig) a = b = mh;
            else if (aBig) a = b;
            else if (bBig) b = a;
            if (a + b < 0.2) continue;
            pairs.Add((pts[i] + normal * a, pts[i] - normal * b));
        }
        return pairs.Count >= 3 ? pairs : null;
    }

    private static double MedianWidth(List<Vec2> pts, List<(Vec2 A, Vec2 B)> segments, bool closed)
    {
        var widths = new List<double>();
        int n = pts.Count;
        for (int i = 0; i < n; i += 2)
        {
            var prev = pts[closed ? (i + n - 1) % n : Math.Max(0, i - 1)];
            var next = pts[closed ? (i + 1) % n : Math.Min(n - 1, i + 1)];
            var normal = (next - prev).Normalized().Perp();
            double a = Ray(pts[i], normal, segments), b = Ray(pts[i], -normal, segments);
            if (!double.IsNaN(a) && !double.IsNaN(b)) widths.Add(a + b);
        }
        if (widths.Count == 0) return double.NaN;
        widths.Sort();
        return widths[widths.Count / 2];
    }

    private static List<Vec2> TrimEnd(List<Vec2> pts, double length, double step, bool atStart)
    {
        int drop = (int)Math.Round(length / step);
        if (pts.Count - drop < 6) return pts;
        return atStart ? pts.Skip(drop).ToList() : pts.Take(pts.Count - drop).ToList();
    }

    /// <summary>Distância (mm) de <paramref name="p"/> até o primeiro contorno na direção <paramref name="dir"/>; NaN se não houver.</summary>
    private static double Ray(Vec2 p, Vec2 dir, List<(Vec2 A, Vec2 B)> segments)
    {
        double best = double.PositiveInfinity;
        foreach (var (a, b) in segments)
        {
            var e = b - a;
            double den = Vec2.Cross(dir, e);
            if (Math.Abs(den) < 1e-12) continue;
            var ap = a - p;
            double t = Vec2.Cross(ap, e) / den;      // distância ao longo do raio
            double u = Vec2.Cross(ap, dir) / den;    // posição no segmento
            if (t > 1e-6 && u >= 0 && u <= 1 && t < best) best = t;
        }
        return double.IsPositiveInfinity(best) ? double.NaN : best;
    }

    private static List<Vec2> ExtendEnd(List<Vec2> pts, List<(Vec2 A, Vec2 B)> segments, double step, bool atStart)
    {
        int n = pts.Count;
        var end = atStart ? pts[0] : pts[^1];
        var inner = atStart ? pts[Math.Min(n - 1, 8)] : pts[Math.Max(0, n - 9)];
        var dir = (end - inner).Normalized();
        if (dir == Vec2.Zero) return pts;
        double reach = Ray(end, dir, segments);
        if (double.IsNaN(reach) || reach < step) return pts;
        reach = Math.Min(reach - 0.05, 4.0);
        var extra = new List<Vec2>();
        for (double d = step; d <= reach; d += step) extra.Add(end + dir * d);
        if (extra.Count == 0) return pts;
        if (atStart) { extra.Reverse(); extra.AddRange(pts); return extra; }
        var result = pts.ToList();
        result.AddRange(extra);
        return result;
    }

    private static List<Vec2> Resample(List<Vec2> poly, double step, bool closed)
    {
        var line = closed ? poly.Append(poly[0]).ToList() : poly;
        var samples = GeometryUtil.SampleUniform(line, step).Select(s => s.Point).ToList();
        if (closed && samples.Count > 1 && Vec2.Distance(samples[0], samples[^1]) < step * 0.5) samples.RemoveAt(samples.Count - 1);
        return samples;
    }

    private static List<Vec2> Smooth(List<Vec2> pts, int radius, bool closed)
    {
        int n = pts.Count;
        var result = new List<Vec2>(n);
        for (int i = 0; i < n; i++)
        {
            int r = closed ? radius : Math.Min(radius, Math.Min(i, n - 1 - i));   // as pontas não encolhem
            double x = 0, y = 0;
            for (int k = -r; k <= r; k++)
            {
                var q = pts[closed ? ((i + k) % n + n) % n : i + k];
                x += q.X; y += q.Y;
            }
            result.Add(new Vec2(x / (2 * r + 1), y / (2 * r + 1)));
        }
        return result;
    }
}
