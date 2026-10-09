using Bordadox.Core.Geometry;

namespace Bordadox.Core.Digitize;

/// <summary>Transforma o contorno em escada de pixels em nós de um caminho: cantos nítidos e trechos suaves.</summary>
public static class Vectorizer
{
    /// <param name="loop">Cantos do contorno, em pixels.</param>
    /// <param name="tolerancePx">Quanto a linha simplificada pode se afastar da escada de pixels.</param>
    /// <param name="cornerDeg">Curvas mais fechadas que isto viram canto.</param>
    /// <param name="toMm">Converte um ponto em pixels para milímetros.</param>
    public static List<PathNode> Fit(IReadOnlyList<PointI> loop, double tolerancePx, double cornerDeg, Func<Vec2, Vec2> toMm)
    {
        var pts = loop.Select(p => new Vec2(p.X, p.Y)).ToList();
        var poly = SimplifyClosed(pts, tolerancePx);
        var nodes = new List<PathNode>(poly.Count);
        double cornerRad = cornerDeg * Math.PI / 180;

        for (int i = 0; i < poly.Count; i++)
        {
            var prev = poly[(i + poly.Count - 1) % poly.Count];
            var cur = poly[i];
            var next = poly[(i + 1) % poly.Count];
            var a = (cur - prev).Normalized();
            var b = (next - cur).Normalized();
            double turn = Math.Acos(Math.Clamp(Vec2.Dot(a, b), -1, 1));
            nodes.Add(new PathNode(toMm(cur), Curve: turn < cornerRad));
        }
        return nodes;
    }

    /// <summary>Caminho aberto (linha central de um traço): simplifica e classifica os nós; as pontas são cantos.</summary>
    public static List<PathNode> FitOpen(IReadOnlyList<PointI> path, double tolerancePx, double cornerDeg, Func<Vec2, Vec2> toMm)
    {
        // Centro do pixel, não o canto.
        var pts = path.Select(p => new Vec2(p.X + 0.5, p.Y + 0.5)).ToList();
        var poly = GeometryUtil.Simplify(pts, tolerancePx);
        double cornerRad = cornerDeg * Math.PI / 180;
        var nodes = new List<PathNode>(poly.Count);
        for (int i = 0; i < poly.Count; i++)
        {
            bool curve = false;
            if (i > 0 && i < poly.Count - 1)
            {
                var a = (poly[i] - poly[i - 1]).Normalized();
                var b = (poly[i + 1] - poly[i]).Normalized();
                curve = Math.Acos(Math.Clamp(Vec2.Dot(a, b), -1, 1)) < cornerRad;
            }
            nodes.Add(new PathNode(toMm(poly[i]), curve));
        }
        return nodes;
    }

    /// <summary>Douglas-Peucker em polígono fechado: divide no ponto mais distante do primeiro e simplifica cada metade.</summary>
    public static List<Vec2> SimplifyClosed(IReadOnlyList<Vec2> pts, double tol)
    {
        if (pts.Count <= 4) return pts.ToList();

        int far = 0;
        double fd = -1;
        for (int i = 1; i < pts.Count; i++)
        {
            double d = Vec2.Distance(pts[0], pts[i]);
            if (d > fd) { fd = d; far = i; }
        }

        var first = pts.Take(far + 1).ToList();
        var second = pts.Skip(far).Append(pts[0]).ToList();
        var a = GeometryUtil.Simplify(first, tol);
        var b = GeometryUtil.Simplify(second, tol);
        var result = new List<Vec2>(a);
        result.AddRange(b.Skip(1).Take(b.Count - 2)); // sem repetir o ponto do meio nem fechar de volta no primeiro
        return result;
    }

    /// <summary>Comprimento do contorno em mm (segmentos retos entre os nós).</summary>
    public static double Perimeter(IReadOnlyList<PathNode> nodes)
    {
        double len = 0;
        for (int i = 0; i < nodes.Count; i++)
            len += Vec2.Distance(nodes[i].P, nodes[(i + 1) % nodes.Count].P);
        return len;
    }

    /// <summary>Área do polígono (mm²), sempre positiva.</summary>
    public static double Area(IReadOnlyList<PathNode> nodes) =>
        Math.Abs(GeometryUtil.SignedArea(nodes.Select(n => n.P).ToList()));
}
