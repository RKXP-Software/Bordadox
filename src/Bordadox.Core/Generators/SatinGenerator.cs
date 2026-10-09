using Bordadox.Core.Geometry;

namespace Bordadox.Core.Generators;

public sealed record SatinSettings
{
    /// <summary>Distância entre pontos consecutivos do mesmo lado, em mm.</summary>
    public double Spacing { get; init; } = 0.4;
    public double PullCompensation { get; init; } = 0.15;
    /// <summary>Pontos maiores que isso são divididos (evita pontos frouxos), em mm.</summary>
    public double MaxStitch { get; init; } = 7;
    public bool AutoSplit { get; init; } = true;
}

/// <summary>Coluna satin entre dois trilhos (A e B). Cada par de nós A[i]/B[i] define a direção dos pontos.</summary>
public static class SatinGenerator
{
    /// <summary>Pares (ponto no trilho A, ponto no trilho B) espaçados ao longo da coluna.</summary>
    public static List<(Vec2 A, Vec2 B)> SamplePairs(IReadOnlyList<PathNode> railA, IReadOnlyList<PathNode> railB, double spacing)
    {
        var pairs = new List<(Vec2, Vec2)>();
        int sections = Math.Min(railA.Count, railB.Count) - 1;
        if (sections < 1) return pairs;

        var polyA = SplitAtNodes(railA, sections);
        var polyB = SplitAtNodes(railB, sections);
        for (int k = 0; k < sections; k++)
        {
            double lenA = GeometryUtil.PolylineLength(polyA[k]);
            double lenB = GeometryUtil.PolylineLength(polyB[k]);
            int n = Math.Max(1, (int)Math.Ceiling(Math.Max(lenA, lenB) / spacing));
            var sa = UniformPoints(polyA[k], n);
            var sb = UniformPoints(polyB[k], n);
            for (int i = k == 0 ? 0 : 1; i <= n; i++) pairs.Add((sa[i], sb[i]));
        }
        return pairs;
    }

    /// <summary>
    /// Pontos satin em zigue-zague. Os pares vêm a cada meia densidade e as penetrações alternam
    /// de lado (A0, B1, A2, B3, ...), então pontos do mesmo lado ficam a <c>Spacing</c> de distância.
    /// </summary>
    public static List<Vec2> Satin(List<(Vec2 A, Vec2 B)> pairs, SatinSettings s)
    {
        var pts = new List<Vec2>();
        for (int i = 0; i < pairs.Count; i++)
        {
            var (a, b) = pairs[i];
            var dir = (b - a).Normalized();
            var point = i % 2 == 0 ? a - dir * s.PullCompensation : b + dir * s.PullCompensation;

            if (pts.Count > 0 && s.AutoSplit)
            {
                var prev = pts[^1];
                double len = Vec2.Distance(prev, point);
                if (len > s.MaxStitch)
                {
                    // Divide e alterna o ponto de divisão entre passadas para não formar um sulco.
                    int parts = (int)Math.Ceiling(len / s.MaxStitch);
                    double shift = (i / 2) % 2 == 0 ? 0 : 0.5;
                    for (int k = 1; k < parts; k++)
                        pts.Add(Vec2.Lerp(prev, point, Math.Clamp((k - shift + 0.25) / parts, 0.1, 0.9)));
                }
            }
            pts.Add(point);
        }
        return pts;
    }

    /// <summary>Corrido no meio da coluna (reforço central).</summary>
    public static List<Vec2> CenterRun(List<(Vec2 A, Vec2 B)> pairs, double stitchLength)
    {
        var mid = pairs.Select(p => Vec2.Lerp(p.A, p.B, 0.5)).ToList();
        return RunGenerator.ResampleSection(mid, stitchLength);
    }

    /// <summary>Corrido rente às bordas (ida pelo trilho A, volta pelo B), recuado <paramref name="inset"/> mm.</summary>
    public static List<Vec2> EdgeRun(List<(Vec2 A, Vec2 B)> pairs, double inset, double stitchLength)
    {
        var a = pairs.Select(p => InsetPair(p, inset).A).ToList();
        var b = pairs.Select(p => InsetPair(p, inset).B).Reverse().ToList();
        var pts = RunGenerator.ResampleSection(a, stitchLength);
        var back = RunGenerator.ResampleSection(b, stitchLength);
        pts.AddRange(back);
        pts.Add(pts[0]);
        return pts;
    }

    /// <summary>Zigue-zague aberto por baixo do satin.</summary>
    public static List<Vec2> ZigzagUnderlay(List<(Vec2 A, Vec2 B)> pairs, double spacing, double inset, double baseSpacing)
    {
        int every = Math.Max(1, (int)Math.Round(spacing / Math.Max(baseSpacing, 0.05)));
        var idx = new List<int>();
        for (int i = 0; i < pairs.Count; i += every) idx.Add(i);
        if (idx[^1] != pairs.Count - 1) idx.Add(pairs.Count - 1);

        var pts = new List<Vec2>();
        for (int k = 0; k < idx.Count; k++)
        {
            var p = InsetPair(pairs[idx[k]], inset);
            pts.Add(k % 2 == 0 ? p.A : p.B);
        }
        return pts;
    }

    private static (Vec2 A, Vec2 B) InsetPair((Vec2 A, Vec2 B) p, double inset)
    {
        var len = Vec2.Distance(p.A, p.B);
        double d = Math.Min(inset, len * 0.3);
        var dir = (p.B - p.A).Normalized();
        return (p.A + dir * d, p.B - dir * d);
    }

    // Trilho achatado e dividido em uma polilinha por seção (entre nós consecutivos).
    private static List<List<Vec2>> SplitAtNodes(IReadOnlyList<PathNode> rail, int sections)
    {
        var result = new List<List<Vec2>>();
        for (int k = 0; k < sections; k++)
        {
            // Achata um trecho de 4 nós (com vizinhos) para manter a tangente correta nas curvas.
            int from = Math.Max(0, k - 1), to = Math.Min(rail.Count - 1, k + 2);
            var sub = rail.Skip(from).Take(to - from + 1).ToList();
            var flat = PathUtil.FlattenSections(sub, false).SelectMany((s, i) => i == 0 ? s : s.Skip(1)).ToList();
            // Recorta só o trecho entre o nó k e o nó k+1.
            int startIdx = IndexOfNear(flat, rail[k].P, 0);
            int endIdx = IndexOfNear(flat, rail[k + 1].P, startIdx);
            result.Add(flat.Skip(startIdx).Take(endIdx - startIdx + 1).ToList());
        }
        return result;
    }

    private static int IndexOfNear(List<Vec2> pts, Vec2 p, int start)
    {
        for (int i = start; i < pts.Count; i++) if (Vec2.Distance(pts[i], p) < 1e-6) return i;
        return pts.Count - 1;
    }

    private static List<Vec2> UniformPoints(List<Vec2> poly, int n)
    {
        if (poly.Count < 2 || GeometryUtil.PolylineLength(poly) < 1e-6)
            return Enumerable.Repeat(poly[0], n + 1).ToList();
        var samples = GeometryUtil.SampleUniform(poly, GeometryUtil.PolylineLength(poly) / n).Select(s => s.Point).ToList();
        // SampleUniform arredonda a contagem; garante exatamente n+1 pontos.
        while (samples.Count < n + 1) samples.Add(poly[^1]);
        if (samples.Count > n + 1) samples = samples.Take(n).Append(poly[^1]).ToList();
        samples[^1] = poly[^1];
        return samples;
    }
}
