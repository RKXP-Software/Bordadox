using Bordadox.Core.Geometry;

namespace Bordadox.Core.Generators;

/// <summary>
/// Acha um caminho curto entre dois pontos que fica DENTRO de uma forma (com furos), contornando os furos.
/// Serve para ligar trechos de um preenchimento por dentro dele (pontos corridos escondidos sob o resto da costura)
/// em vez de saltar e cortar a linha. Usa uma grade fina (A*) e depois "estica" o caminho em retas.
/// </summary>
public sealed class InsideRouter
{
    private const int StampStart = 1;

    private readonly double _x0, _y0, _cell;
    private readonly int _w, _h;
    private readonly bool[] _ok;          // célula dentro da forma e longe da borda
    private readonly float[] _g;
    private readonly int[] _stamp, _parent;
    private int _query = StampStart;

    public InsideRouter(IReadOnlyList<IReadOnlyList<Vec2>> contours, double cell = 0.25)
    {
        _cell = cell;
        var all = contours.Where(c => c.Count >= 3).SelectMany(c => c).ToList();
        var box = Rect2.FromPoints(all).Inflate(cell * 2);
        _x0 = box.MinX; _y0 = box.MinY;
        _w = Math.Max(3, (int)Math.Ceiling(box.Width / cell));
        _h = Math.Max(3, (int)Math.Ceiling(box.Height / cell));

        var inside = new bool[_w * _h];
        var xs = new List<double>();
        for (int y = 0; y < _h; y++)
        {
            double wy = _y0 + (y + 0.5) * cell;
            xs.Clear();
            foreach (var c in contours)
            {
                if (c.Count < 3) continue;
                for (int i = 0; i < c.Count; i++)
                {
                    var a = c[i]; var b = c[(i + 1) % c.Count];
                    if ((a.Y <= wy && b.Y > wy) || (b.Y <= wy && a.Y > wy))
                        xs.Add(a.X + (wy - a.Y) * (b.X - a.X) / (b.Y - a.Y));
                }
            }
            xs.Sort();
            for (int k = 0; k + 1 < xs.Count; k += 2)
            {
                int xa = (int)Math.Ceiling((xs[k] - _x0) / cell - 0.5), xb = (int)Math.Floor((xs[k + 1] - _x0) / cell - 0.5);
                for (int x = Math.Max(0, xa); x <= Math.Min(_w - 1, xb); x++) inside[y * _w + x] = true;
            }
        }

        // Recua uma célula da borda: o caminho não encosta no contorno (onde a costura de cima termina).
        _ok = new bool[inside.Length];
        for (int y = 1; y < _h - 1; y++)
            for (int x = 1; x < _w - 1; x++)
            {
                int i = y * _w + x;
                _ok[i] = inside[i] && inside[i - 1] && inside[i + 1] && inside[i - _w] && inside[i + _w];
            }
        _g = new float[inside.Length];
        _stamp = new int[inside.Length];
        _parent = new int[inside.Length];
    }

    private int CellOf(Vec2 p)
    {
        int x = (int)Math.Floor((p.X - _x0) / _cell), y = (int)Math.Floor((p.Y - _y0) / _cell);
        return x < 0 || y < 0 || x >= _w || y >= _h ? -1 : y * _w + x;
    }

    private Vec2 Center(int i) => new(_x0 + (i % _w + 0.5) * _cell, _y0 + (i / _w + 0.5) * _cell);

    /// <summary>A célula livre mais próxima do ponto (até 3 células de distância), ou -1.</summary>
    private int Snap(Vec2 p)
    {
        int c = CellOf(p);
        if (c < 0) return -1;
        if (_ok[c]) return c;
        int cx = c % _w, cy = c / _w, best = -1;
        double bestD = double.MaxValue;
        for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (x < 0 || y < 0 || x >= _w || y >= _h || !_ok[y * _w + x]) continue;
                double d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = y * _w + x; }
            }
        return best;
    }

    private bool LineOfSight(Vec2 a, Vec2 b)
    {
        double d = Vec2.Distance(a, b);
        int n = Math.Max(1, (int)Math.Ceiling(d / (_cell * 0.5)));
        for (int k = 0; k <= n; k++)
        {
            int c = CellOf(Vec2.Lerp(a, b, (double)k / n));
            if (c < 0 || !_ok[c]) return false;
        }
        return true;
    }

    /// <summary>
    /// Caminho de <paramref name="a"/> até <paramref name="b"/> dentro da forma, com no máximo
    /// <paramref name="maxLength"/> mm. Devolve os pontos intermediários (sem repetir a e b), ou null se não houver.
    /// </summary>
    public List<Vec2>? Route(Vec2 a, Vec2 b, double maxLength)
    {
        int s = Snap(a), t = Snap(b);
        if (s < 0 || t < 0) return null;
        if (s == t) return new List<Vec2>();

        int query = ++_query;
        var open = new PriorityQueue<int, float>();
        _g[s] = 0; _stamp[s] = query; _parent[s] = -1;
        open.Enqueue(s, 0);
        var target = Center(t);
        float limit = (float)(maxLength / _cell);
        bool found = false;

        while (open.TryDequeue(out int cur, out _))
        {
            if (cur == t) { found = true; break; }
            int cx = cur % _w, cy = cur / _w;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int x = cx + dx, y = cy + dy;
                    if (x < 0 || y < 0 || x >= _w || y >= _h) continue;
                    int n = y * _w + x;
                    if (!_ok[n]) continue;
                    // Diagonal só se os dois lados também estão livres (não corta quina de furo).
                    if (dx != 0 && dy != 0 && (!_ok[cy * _w + x] || !_ok[y * _w + cx])) continue;
                    float g = _g[cur] + (dx != 0 && dy != 0 ? 1.4142f : 1f);
                    var c = Center(n);
                    float h = (float)(Math.Sqrt((c.X - target.X) * (c.X - target.X) + (c.Y - target.Y) * (c.Y - target.Y)) / _cell);
                    if (g + h > limit) continue;
                    if (_stamp[n] == query && _g[n] <= g) continue;
                    _g[n] = g; _stamp[n] = query; _parent[n] = cur;
                    open.Enqueue(n, g + h);
                }
        }
        if (!found) return null;

        var cells = new List<Vec2>();
        for (int i = t; i >= 0; i = _parent[i]) cells.Add(Center(i));
        cells.Reverse();

        // Estica o caminho: de cada ponto, vai direto ao mais distante que ainda enxerga por dentro.
        var pts = new List<Vec2> { a };
        pts.AddRange(cells);
        pts.Add(b);
        var result = new List<Vec2>();
        int from = 0;
        while (from < pts.Count - 1)
        {
            int to = pts.Count - 1;
            while (to > from + 1 && !LineOfSight(pts[from], pts[to])) to--;
            if (to < pts.Count - 1) result.Add(pts[to]);
            from = to;
        }
        return result;
    }
}
