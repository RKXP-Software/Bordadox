using Bordadox.Core.Geometry;

namespace Bordadox.Core.Generators;

public sealed record FillSettings
{
    /// <summary>Ângulo das linhas de costura em graus (0 = horizontal).</summary>
    public double AngleDeg { get; init; } = 45;
    /// <summary>Distância entre linhas, em mm (densidade).</summary>
    public double Spacing { get; init; } = 0.4;
    /// <summary>Comprimento máximo de ponto dentro da linha (tatami), em mm.</summary>
    public double StitchLength { get; init; } = 3.5;
    /// <summary>true = cada linha é um único ponto de borda a borda (satin), dividido se passar de <see cref="MaxSatinLength"/>.</summary>
    public bool Satin { get; init; }
    public double MaxSatinLength { get; init; } = 7;
    /// <summary>Quanto cada linha avança além da borda, para compensar o encolhimento do tecido (mm).</summary>
    public double PullCompensation { get; init; }
    /// <summary>Quanto cada linha recua da borda (usado no reforço por baixo), em mm.</summary>
    public double Inset { get; init; }
}

/// <summary>
/// Preenchimento por linhas paralelas (scanline) com suporte a furos (regra par-ímpar).
/// A forma é dividida em regiões contínuas; cada região é costurada em zigue-zague sem saltos.
/// </summary>
public static class FillGenerator
{
    private readonly record struct Segment(double X0, double X1, double Y, int Row);

    public static List<List<Vec2>> Fill(IReadOnlyList<IReadOnlyList<Vec2>> contours, FillSettings s, Vec2? from = null)
    {
        var result = new List<List<Vec2>>();
        if (contours.Count == 0 || contours[0].Count < 3 || s.Spacing <= 0.01) return result;

        double rad = s.AngleDeg * Math.PI / 180;
        var rotated = contours.Where(c => c.Count >= 3).Select(c => c.Select(p => p.Rotate(-rad)).ToList()).ToList();

        var rows = BuildRows(rotated, s);
        var regions = BuildRegions(rows);

        var pos = from?.Rotate(-rad);
        var remaining = regions.ToList();
        while (remaining.Count > 0)
        {
            // Escolhe a próxima região (e o sentido de percorrê-la) mais perto da posição atual.
            int bestIdx = 0;
            bool bestReverse = false, bestRight = false;
            if (pos != null)
            {
                double bestD = double.PositiveInfinity;
                for (int i = 0; i < remaining.Count; i++)
                {
                    foreach (var (reverseRows, startRight) in new[] { (false, false), (false, true), (true, false), (true, true) })
                    {
                        double d = Vec2.Distance(pos.Value, RegionStart(remaining[i], reverseRows, startRight));
                        if (d < bestD) { bestD = d; bestIdx = i; bestReverse = reverseRows; bestRight = startRight; }
                    }
                }
            }
            // Sem referência: segue a ordem natural (de cima para baixo, da esquerda para a direita).
            var best = StitchRegion(remaining[bestIdx], bestReverse, bestRight, s);
            remaining.RemoveAt(bestIdx);
            if (best.Count == 0) continue;
            pos = best[^1];
            result.Add(best.Select(p => p.Rotate(rad)).ToList());
        }
        return result;
    }

    /// <summary>
    /// Junta trechos consecutivos num só quando o caminho reto do fim de um ao início do próximo
    /// fica inteiro dentro da forma (e é curto). A ligação vira pontos corridos em vez de salto + corte.
    /// Trajetos que cruzariam um furo ou saíssem da forma continuam separados.
    /// </summary>
    public static List<List<Vec2>> ConnectInside(IReadOnlyList<List<Vec2>> runs, IReadOnlyList<IReadOnlyList<Vec2>> contours,
        double maxConnectMm = 8, double stitchLength = 3)
    {
        var result = new List<List<Vec2>>();
        InsideRouter? router = null;   // criado só se algum trecho precisar contornar um furo
        foreach (var run in runs)
        {
            if (run.Count == 0) continue;
            if (result.Count > 0)
            {
                var last = result[^1];
                var a = last[^1];
                var b = run[0];
                double d = Vec2.Distance(a, b);
                if (d <= maxConnectMm && InsideAlong(a, b, d, contours))
                {
                    int n = (int)Math.Ceiling(d / stitchLength);
                    for (int k = 1; k < n; k++) last.Add(Vec2.Lerp(a, b, (double)k / n));
                    last.AddRange(run);
                    continue;
                }
                // Reta direta sai da forma (há um furo no meio): contorna por dentro, se o desvio for curto.
                if (d <= maxConnectMm * 1.5 && d > 0.5)
                {
                    router ??= new InsideRouter(contours);
                    var way = router.Route(a, b, maxConnectMm * 1.5);
                    if (way != null)
                    {
                        var from = a;
                        foreach (var p in way.Append(b))
                        {
                            double seg = Vec2.Distance(from, p);
                            int parts = Math.Max(1, (int)Math.Ceiling(seg / stitchLength));
                            for (int k = 1; k <= parts; k++) if (!(k == parts && p == b)) last.Add(Vec2.Lerp(from, p, (double)k / parts));
                            from = p;
                        }
                        last.AddRange(run);
                        continue;
                    }
                }
            }
            result.Add(run.ToList());
        }
        return result;
    }

    private static bool InsideAlong(Vec2 a, Vec2 b, double d, IReadOnlyList<IReadOnlyList<Vec2>> contours)
    {
        if (d < 0.01) return true;
        int samples = Math.Max(1, (int)Math.Ceiling(d / 0.3));
        for (int k = 1; k < samples; k++)
        {
            var p = Vec2.Lerp(a, b, (double)k / samples);
            if (GeometryUtil.PointInContours(p, contours)) continue;
            // Os pontos de entrada e saída dos trechos passam da borda (compensação de puxada): aceita até 0,4 mm de folga.
            if (contours.Any(c => GeometryUtil.DistanceToPolyline(p, c, true) <= EdgeSlackMm)) continue;
            return false;
        }
        return true;
    }

    private const double EdgeSlackMm = 0.4;

    private static List<List<Segment>> BuildRows(List<List<Vec2>> contours, FillSettings s)
    {
        double minY = contours.SelectMany(c => c).Min(p => p.Y);
        double maxY = contours.SelectMany(c => c).Max(p => p.Y);
        var rows = new List<List<Segment>>();
        var xs = new List<double>();
        double adjust = s.PullCompensation - s.Inset;

        int row = 0;
        for (double y = minY + s.Spacing / 2; y < maxY; y += s.Spacing, row++)
        {
            xs.Clear();
            foreach (var c in contours)
            {
                for (int i = 0; i < c.Count; i++)
                {
                    var a = c[i];
                    var b = c[(i + 1) % c.Count];
                    if ((a.Y <= y && b.Y > y) || (b.Y <= y && a.Y > y))
                        xs.Add(a.X + (y - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                }
            }
            xs.Sort();
            var segs = new List<Segment>();
            for (int i = 0; i + 1 < xs.Count; i += 2)
            {
                double x0 = xs[i] - adjust, x1 = xs[i + 1] + adjust;
                if (x1 - x0 >= 0.3) segs.Add(new Segment(x0, x1, y, row));
            }
            rows.Add(segs);
        }
        return rows;
    }

    /// <summary>Agrupa segmentos de linhas consecutivas que se sobrepõem em regiões costuráveis sem salto.</summary>
    private static List<List<Segment>> BuildRegions(List<List<Segment>> rows)
    {
        var used = rows.Select(r => new bool[r.Count]).ToList();
        var regions = new List<List<Segment>>();
        for (int r = 0; r < rows.Count; r++)
        {
            for (int k = 0; k < rows[r].Count; k++)
            {
                if (used[r][k]) continue;
                var region = new List<Segment> { rows[r][k] };
                used[r][k] = true;
                var cur = rows[r][k];
                for (int rr = r + 1; rr < rows.Count; rr++)
                {
                    int next = -1, overlapping = 0;
                    for (int j = 0; j < rows[rr].Count; j++)
                    {
                        var c = rows[rr][j];
                        if (c.X0 < cur.X1 && c.X1 > cur.X0)
                        {
                            overlapping++;
                            if (next < 0 && !used[rr][j]) next = j;
                        }
                    }
                    // Para numa bifurcação (a forma se divide em dois ramos): cada ramo vira sua própria região.
                    if (next < 0 || overlapping > 1) break;
                    // Também para numa junção (dois ramos de cima se encontram nesta linha).
                    var cand = rows[rr][next];
                    int above = rows[rr - 1].Count(c => c.X0 < cand.X1 && c.X1 > cand.X0);
                    if (above > 1) break;
                    used[rr][next] = true;
                    region.Add(cand);
                    cur = cand;
                }
                regions.Add(region);
            }
        }
        return regions;
    }

    private static Vec2 RegionStart(List<Segment> region, bool reverseRows, bool startRight)
    {
        var seg = reverseRows ? region[^1] : region[0];
        return new Vec2(startRight ? seg.X1 : seg.X0, seg.Y);
    }

    private static List<Vec2> StitchRegion(List<Segment> region, bool reverseRows, bool startRight, FillSettings s)
    {
        var pts = new List<Vec2>();
        bool rightToLeft = startRight;
        for (int i = 0; i < region.Count; i++)
        {
            var seg = region[reverseRows ? region.Count - 1 - i : i];
            double from = rightToLeft ? seg.X1 : seg.X0;
            double to = rightToLeft ? seg.X0 : seg.X1;

            // Ligação com a linha anterior: se a borda deslocou muito, caminha em passos curtos.
            if (pts.Count > 0)
            {
                var last = pts[^1];
                var target = new Vec2(from, seg.Y);
                double gap = Vec2.Distance(last, target);
                int n = (int)Math.Ceiling(gap / s.StitchLength);
                for (int k = 1; k < n; k++) pts.Add(Vec2.Lerp(last, target, (double)k / n));
            }

            pts.Add(new Vec2(from, seg.Y));
            foreach (var x in InnerPoints(seg, s, rightToLeft)) pts.Add(new Vec2(x, seg.Y));
            pts.Add(new Vec2(to, seg.Y));
            rightToLeft = !rightToLeft;
        }
        return pts;
    }

    // Penetrações intermediárias da linha. Tatami: grade com deslocamento de 1/3 a cada linha (padrão de tijolo).
    private static IEnumerable<double> InnerPoints(Segment seg, FillSettings s, bool rightToLeft)
    {
        var xs = new List<double>();
        double len = seg.X1 - seg.X0;
        if (s.Satin)
        {
            if (len > s.MaxSatinLength)
            {
                int n = (int)Math.Ceiling(len / s.MaxSatinLength);
                double shift = seg.Row % 2 == 0 ? 0 : 0.5;
                for (int k = 1; k < n; k++) xs.Add(seg.X0 + len * Math.Clamp((k - shift + 0.25) / n, 0.1, 0.9));
            }
        }
        else
        {
            double step = s.StitchLength;
            double offset = (seg.Row % 3) * step / 3;
            // Margem pequena: também em linhas curtas (formas estreitas) aparece penetração intermediária,
            // que é o que diferencia o tatami (textura escalonada) do satin (ponto de borda a borda).
            double margin = step * 0.15;
            double first = Math.Ceiling((seg.X0 + margin - offset) / step) * step + offset;
            for (double x = first; x < seg.X1 - margin; x += step) xs.Add(x);
        }
        if (rightToLeft) xs.Reverse();
        return xs;
    }
}
