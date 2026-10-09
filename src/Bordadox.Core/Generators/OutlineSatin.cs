using Bordadox.Core.Geometry;

namespace Bordadox.Core.Generators;

/// <summary>
/// Pares de pontos (lado A, lado B) ao longo de um contorno fechado, para costurar uma faixa satin de largura fixa centrada nele
/// (acabamento de aplique, borda de forma). Nas quinas vivas os pares giram em leque em torno do vértice, como numa quina
/// digitalizada à mão; nas curvas suaves a normal acompanha o contorno.
/// </summary>
public static class OutlineSatin
{
    /// <summary>Quina com giro maior que isto (graus) vira leque.</summary>
    private const double CornerDegrees = 25;
    private const double FanStepDegrees = 12;

    public static List<(Vec2 A, Vec2 B)> Pairs(IReadOnlyList<Vec2> contour, double width, double step)
    {
        step = Math.Max(0.02, step);
        var pairs = new List<(Vec2, Vec2)>();
        var ring = Dedupe(contour);
        if (ring.Count < 3) return pairs;

        var dense = GeometryUtil.ResamplePreservingCorners(ring.Append(ring[0]).ToList(), step);
        dense.RemoveAt(dense.Count - 1);                       // o último repete o primeiro
        int n = dense.Count;
        double half = width / 2;

        for (int i = 0; i < n; i++)
        {
            var prev = dense[(i + n - 1) % n];
            var cur = dense[i];
            var next = dense[(i + 1) % n];
            var dIn = (cur - prev).Normalized();
            var dOut = (next - cur).Normalized();
            if (dIn == Vec2.Zero || dOut == Vec2.Zero) continue;

            var nIn = dIn.Perp();
            var nOut = dOut.Perp();
            double turn = Math.Atan2(Vec2.Cross(dIn, dOut), Vec2.Dot(dIn, dOut));   // com sinal, radianos
            double turnDeg = Math.Abs(turn) * 180 / Math.PI;

            if (turnDeg >= CornerDegrees)
            {
                // Leque: a normal gira da de entrada até a de saída, todos os pares no mesmo vértice.
                int k = Math.Max(2, (int)Math.Ceiling(turnDeg / FanStepDegrees));
                for (int j = 0; j <= k; j++)
                {
                    var normal = nIn.Rotate(turn * j / k);
                    pairs.Add((cur + normal * half, cur - normal * half));
                }
            }
            else
            {
                // Normal média das duas direções, alongada para manter a largura da faixa (limitada para não disparar).
                var normal = (nIn + nOut).Normalized();
                double cos = Math.Max(0.77, Vec2.Dot(normal, nIn));
                var scaled = normal * (half / cos);
                pairs.Add((cur + scaled, cur - scaled));
            }
        }
        return pairs;
    }

    /// <summary>Zigue-zague de fixação: alterna entre os dois lados de uma faixa estreita em volta do contorno.</summary>
    public static List<Vec2> Zigzag(IReadOnlyList<Vec2> contour, double width, double step)
    {
        var pairs = Pairs(contour, width, step);
        var pts = new List<Vec2>();
        for (int i = 0; i < pairs.Count; i++) pts.Add(i % 2 == 0 ? pairs[i].A : pairs[i].B);
        if (pts.Count > 0) pts.Add(pts[0]);
        return pts;
    }

    private static List<Vec2> Dedupe(IReadOnlyList<Vec2> pts)
    {
        var result = new List<Vec2>();
        foreach (var p in pts)
            if (result.Count == 0 || Vec2.Distance(result[^1], p) > 1e-6) result.Add(p);
        if (result.Count > 1 && Vec2.Distance(result[0], result[^1]) < 1e-6) result.RemoveAt(result.Count - 1);
        return result;
    }
}
