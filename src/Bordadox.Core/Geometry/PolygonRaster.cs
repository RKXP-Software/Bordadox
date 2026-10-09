namespace Bordadox.Core.Geometry;

/// <summary>Rasteriza polígonos (regra par-ímpar, então contornos internos viram furos) numa grade de células quadradas.</summary>
public static class PolygonRaster
{
    /// <summary>Máscara de <paramref name="w"/> × <paramref name="h"/> células de lado <paramref name="cell"/> com canto superior esquerdo em (x0, y0).</summary>
    public static bool[] Fill(IReadOnlyList<IReadOnlyList<Vec2>> contours, double x0, double y0, double cell, int w, int h)
    {
        var mask = new bool[w * h];
        var xs = new List<double>();
        for (int y = 0; y < h; y++)
        {
            double wy = y0 + (y + 0.5) * cell;
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
                int xa = (int)Math.Ceiling((xs[k] - x0) / cell - 0.5), xb = (int)Math.Floor((xs[k + 1] - x0) / cell - 0.5);
                for (int x = Math.Max(0, xa); x <= Math.Min(w - 1, xb); x++) mask[y * w + x] = true;
            }
        }
        return mask;
    }
}
