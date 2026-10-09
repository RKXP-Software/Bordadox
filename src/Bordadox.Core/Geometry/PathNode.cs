namespace Bordadox.Core.Geometry;

/// <summary>Nó de um caminho digitalizado. <see cref="Curve"/> = ponto de curva (clique direito); senão, canto (clique esquerdo).</summary>
public readonly record struct PathNode(Vec2 P, bool Curve = false);

public static class PathUtil
{
    /// <summary>Converte nós em polilinha. Trechos ligados a nós de curva viram splines (Catmull-Rom).</summary>
    public static List<Vec2> Flatten(IReadOnlyList<PathNode> nodes, bool closed)
    {
        var result = new List<Vec2>();
        foreach (var section in FlattenSections(nodes, closed))
        {
            if (result.Count > 0 && section.Count > 0 && Vec2.Distance(result[^1], section[0]) < 1e-9)
                result.AddRange(section.Skip(1));
            else result.AddRange(section);
        }
        if (closed && result.Count > 1 && Vec2.Distance(result[0], result[^1]) < 1e-9) result.RemoveAt(result.Count - 1);
        return result;
    }

    /// <summary>
    /// Polilinhas entre nós de canto. Cada seção é suave por dentro (só passa por nós de curva),
    /// o que permite distribuir os pontos de costura uniformemente nas curvas e manter os cantos exatos.
    /// Em caminho fechado, a última seção termina no primeiro nó.
    /// </summary>
    public static List<List<Vec2>> FlattenSections(IReadOnlyList<PathNode> nodes, bool closed)
    {
        var sections = new List<List<Vec2>>();
        int n = nodes.Count;
        if (n == 0) return sections;
        if (n == 1) { sections.Add(new List<Vec2> { nodes[0].P }); return sections; }

        int segCount = closed ? n : n - 1;
        // Em caminho fechado, começa num canto (se houver) para que as seções fiquem inteiras.
        int start = 0;
        if (closed)
        {
            int corner = Enumerable.Range(0, n).FirstOrDefault(i => !nodes[i].Curve, -1);
            start = corner < 0 ? 0 : corner;
        }

        List<Vec2>? current = null;
        for (int s = 0; s < segCount; s++)
        {
            int i = (start + s) % n;
            int j = (i + 1) % n;
            if (current == null)
            {
                current = new List<Vec2> { nodes[i].P };
                sections.Add(current);
            }
            AppendSegment(current, nodes, i, j, closed);
            if (!nodes[j].Curve) current = null;
        }
        return sections;
    }

    private static void AppendSegment(List<Vec2> output, IReadOnlyList<PathNode> nodes, int i, int j, bool closed)
    {
        var a = nodes[i];
        var b = nodes[j];
        if (!a.Curve && !b.Curve)
        {
            output.Add(b.P);
            return;
        }
        var m0 = Tangent(nodes, i, j, closed, atStart: true);
        var m1 = Tangent(nodes, j, i, closed, atStart: false);
        double len = Vec2.Distance(a.P, b.P);
        int steps = Math.Clamp((int)Math.Ceiling(len / 0.4), 4, 200);
        for (int s = 1; s <= steps; s++)
        {
            double t = (double)s / steps, t2 = t * t, t3 = t2 * t;
            var p = a.P * (2 * t3 - 3 * t2 + 1) + m0 * (t3 - 2 * t2 + t) + b.P * (-2 * t3 + 3 * t2) + m1 * (t3 - t2);
            output.Add(p);
        }
    }

    // Tangente de Hermite no nó k, para o segmento entre k e "other".
    // A direção vem dos dois vizinhos, mas o comprimento é sempre o do próprio segmento: assim um segmento curto
    // ao lado de um longo não "estoura" (como acontecia com a média simples dos vizinhos).
    private static Vec2 Tangent(IReadOnlyList<PathNode> nodes, int k, int other, bool closed, bool atStart)
    {
        var node = nodes[k];
        var chord = nodes[other].P - node.P;
        double len = chord.Length;
        if (!node.Curve || len < 1e-12)
        {
            // Canto: a curva sai/chega apontando para o outro extremo do segmento.
            return chord * (atStart ? 0.5 : -0.5);
        }
        int n = nodes.Count;
        var prev = k > 0 ? nodes[k - 1].P : closed ? nodes[n - 1].P : node.P;
        var next = k < n - 1 ? nodes[k + 1].P : closed ? nodes[0].P : node.P;
        var dir = (next - node.P).Normalized() + (node.P - prev).Normalized();
        dir = dir.Length < 1e-9 ? (next - prev).Normalized() : dir.Normalized();
        return dir * len;
    }

    public static List<PathNode> Transformed(IEnumerable<PathNode> nodes, Func<Vec2, Vec2> f) =>
        nodes.Select(n => n with { P = f(n.P) }).ToList();
}
