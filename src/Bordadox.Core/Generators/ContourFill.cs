using Bordadox.Core.Geometry;
using Clipper2Lib;

namespace Bordadox.Core.Generators;

/// <summary>
/// Preenchimento que segue o contorno: anéis concêntricos, cada um um deslocamento para dentro do anterior
/// (e para fora dos furos), costurados do externo para o interno. A direção dos pontos acompanha a forma
/// em vez de ser uma linha reta de um ângulo só. O deslocamento dos contornos é feito com Clipper2.
/// </summary>
public static class ContourFill
{
    private const int MaxRings = 600;

    /// <summary>Anéis de cada nível: nível 0 é o contorno recuado meia densidade; cada nível seguinte recua mais uma densidade.</summary>
    public static List<List<List<Vec2>>> Rings(IReadOnlyList<IReadOnlyList<Vec2>> contours, double spacing, double firstInset)
    {
        spacing = Math.Max(FillGenerator.MinSpacing, spacing);
        var levels = new List<List<List<Vec2>>>();
        if (contours.Count == 0 || spacing < 0.05) return levels;

        // Clipper2: caminho externo com área positiva, furos com área negativa (como convenção de orientação).
        var paths = new PathsD();
        for (int i = 0; i < contours.Count; i++)
        {
            if (contours[i].Count < 3) continue;
            var path = new PathD(contours[i].Select(p => new PointD(p.X, p.Y)));
            if (Clipper.IsPositive(path) != (i == 0)) path.Reverse();
            paths.Add(path);
        }
        if (paths.Count == 0) return levels;

        for (int k = 0; k < MaxRings; k++)
        {
            double delta = -(firstInset + k * spacing);
            var offset = Clipper.InflatePaths(paths, delta, JoinType.Round, EndType.Polygon, 2.0, 3, 0.02);
            var rings = ToRings(offset, spacing);
            if (rings.Count == 0) break;
            levels.Add(rings);
        }

        // Onde os anéis de fora e de dentro (do furo) se encontram sobra uma fresta de até duas densidades: um anel a meio passo a fecha.
        if (levels.Count > 0 && levels.Count < MaxRings)
        {
            double delta = -(firstInset + (levels.Count - 0.5) * spacing);
            var half = ToRings(Clipper.InflatePaths(paths, delta, JoinType.Round, EndType.Polygon, 2.0, 3, 0.02), spacing);
            if (half.Count > 0) levels.Add(half);
        }
        return levels;
    }

    /// <summary>Anéis de um deslocamento; descarta pedacinhos (restos de contorno que quase sumiu) menores que umas poucas densidades.</summary>
    private static List<List<Vec2>> ToRings(PathsD offset, double spacing) =>
        offset.Where(p => p.Count >= 3)
            .Select(p => p.Select(q => new Vec2(q.x, q.y)).ToList())
            .Where(r => GeometryUtil.PolylineLength(r.Append(r[0]).ToList()) >= 8 * spacing)
            .ToList();

    /// <summary>
    /// Costura dos anéis, do externo ao interno. O fim de cada anel se liga ao começo do próximo quando estão perto
    /// (a ligação é de uns poucos décimos de mm, coberta pelos pontos vizinhos), formando um espiral contínuo.
    /// </summary>
    public static List<List<Vec2>> Generate(IReadOnlyList<IReadOnlyList<Vec2>> contours, double spacing, double stitchLength, Vec2? from = null)
    {
        spacing = Math.Max(FillGenerator.MinSpacing, spacing);
        stitchLength = Math.Max(FillGenerator.MinStitchLength, stitchLength);
        var runs = new List<List<Vec2>>();
        var levels = Rings(contours, spacing, spacing / 2);
        List<Vec2>? current = null;
        Vec2? pos = from;
        double joinMax = Math.Max(2.5 * spacing, 1.2);

        for (int level = 0; level < levels.Count; level++)
        {
            var pending = levels[level].ToList();
            while (pending.Count > 0)
            {
                // Próximo anel: o que tem um ponto mais perto da agulha.
                int best = 0, bestIdx = 0;
                double bestD = double.MaxValue;
                if (pos is { } p)
                {
                    for (int r = 0; r < pending.Count; r++)
                        for (int i = 0; i < pending[r].Count; i++)
                        {
                            double d = Vec2.Distance(p, pending[r][i]);
                            if (d < bestD) { bestD = d; best = r; bestIdx = i; }
                        }
                }
                var ring = pending[best];
                pending.RemoveAt(best);

                // Gira o anel para começar no ponto mais perto e o fecha no mesmo ponto; o início anda um pouco a cada nível,
                // para as ligações não formarem uma "costura" visível no mesmo lugar.
                var rotated = ring.Skip(bestIdx).Concat(ring.Take(bestIdx)).ToList();
                rotated.Add(rotated[0]);
                var stitched = RunGenerator.ResampleSection(rotated, stitchLength);

                if (current != null && pos is { } last && Vec2.Distance(last, stitched[0]) <= joinMax) current.AddRange(stitched);
                else
                {
                    current = stitched;
                    runs.Add(current);
                }
                pos = current[^1];
            }
        }
        return runs;
    }
}
