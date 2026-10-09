using Bordadox.Core.Geometry;

namespace Bordadox.Core.Generators;

public static class RunGenerator
{
    /// <summary>
    /// Ponto corrido sobre um caminho: cantos são respeitados e cada trecho curvo recebe pontos
    /// de comprimento uniforme (no máximo <paramref name="stitchLength"/>).
    /// </summary>
    public static List<Vec2> Run(IReadOnlyList<PathNode> nodes, bool closed, double stitchLength)
    {
        var result = new List<Vec2>();
        foreach (var section in PathUtil.FlattenSections(nodes, closed))
        {
            var pts = ResampleSection(section, stitchLength);
            if (result.Count > 0) pts.RemoveAt(0);
            result.AddRange(pts);
        }
        return result;
    }

    /// <summary>Divide uma polilinha suave em pontos de comprimento uniforme ao longo do arco.</summary>
    public static List<Vec2> ResampleSection(IReadOnlyList<Vec2> section, double stitchLength)
    {
        if (section.Count < 2) return section.ToList();
        var total = GeometryUtil.PolylineLength(section);
        if (total < 1e-6) return new List<Vec2> { section[0] };
        int n = Math.Max(1, (int)Math.Ceiling(total / stitchLength - 1e-9));
        var samples = GeometryUtil.SampleUniform(section, total / n);
        var pts = samples.Select(s => s.Point).ToList();
        pts[^1] = section[^1];
        return pts;
    }

    /// <summary>Ponto triplo (bean): cada ponto é costurado ida-volta-ida.</summary>
    public static List<Vec2> Triple(IReadOnlyList<Vec2> pts)
    {
        var result = new List<Vec2>();
        if (pts.Count == 0) return result;
        result.Add(pts[0]);
        for (int i = 1; i < pts.Count; i++)
        {
            result.Add(pts[i]);
            result.Add(pts[i - 1]);
            result.Add(pts[i]);
        }
        return result;
    }
}
