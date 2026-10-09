using System.ComponentModel;
using Bordadox.Core.Geometry;

namespace Bordadox.Core.Generators;

public enum FillPattern
{
    [Description("Onda")] Wave,
    [Description("Zigue-zague")] Zigzag,
    [Description("Losangos")] Diamond,
    [Description("Cruzado")] Crosshatch,
}

/// <summary>Preenchimentos decorativos: um motivo que se repete ao longo das linhas da forma, em vez de pontos retos lado a lado.</summary>
public static class PatternFill
{
    public static List<List<Vec2>> Generate(IReadOnlyList<IReadOnlyList<Vec2>> contours, FillPattern pattern, double size, double angleDeg, double stitchLength, Vec2? from = null)
    {
        size = Math.Max(1, size);
        if (pattern == FillPattern.Crosshatch)
        {
            var runs = new List<List<Vec2>>();
            foreach (var a in new[] { angleDeg, angleDeg + 90 })
            {
                var layer = FillGenerator.Fill(contours, new FillSettings { AngleDeg = a, Spacing = size / 2, StitchLength = stitchLength }, from);
                if (layer.Count > 0) from = layer[^1][^1];
                runs.AddRange(layer);
            }
            return FillGenerator.ConnectInside(runs, contours);
        }

        double rad = angleDeg * Math.PI / 180;
        var rows = FillGenerator.Rows(contours, angleDeg, size);
        var result = new List<List<Vec2>>();
        double amplitude = pattern == FillPattern.Diamond ? size / 2 : size * 0.35;
        // Onda: um ciclo a cada 1,5x o tamanho. Zigue-zague: um ciclo por tamanho. Losangos: ciclo de 2x, a 45°.
        double wavelength = pattern switch { FillPattern.Wave => size * 1.5, FillPattern.Zigzag => size, _ => size * 2 };

        foreach (var seg in rows)
        {
            // Linhas vizinhas em oposição de fase formam losangos; nas outras, todas na mesma fase.
            double phase = pattern == FillPattern.Diamond && seg.Row % 2 == 1 ? Math.PI : 0;
            var path = pattern == FillPattern.Wave
                ? Wave(seg, amplitude, wavelength, phase)
                : Zigzag(seg, amplitude, wavelength, phase);

            // Só os trechos dentro da forma: o motivo passa um pouco da linha e pode sair do contorno perto da borda.
            var piece = new List<Vec2>();
            foreach (var q in path)
            {
                var world = q.Rotate(rad);
                if (GeometryUtil.PointInContours(world, contours)) piece.Add(world);
                else { Flush(result, piece); piece = new List<Vec2>(); }
            }
            Flush(result, piece);
        }
        return FillGenerator.ConnectInside(result, contours);
    }

    private static void Flush(List<List<Vec2>> runs, List<Vec2> piece)
    {
        if (piece.Count >= 2) runs.Add(piece);
    }

    private static IEnumerable<Vec2> Wave(FillGenerator.RowSegment seg, double amplitude, double wavelength, double phase)
    {
        const double Step = 0.8;
        double len = seg.X1 - seg.X0;
        int n = Math.Max(2, (int)Math.Ceiling(len / Step));
        for (int i = 0; i <= n; i++)
        {
            double x = seg.X0 + len * i / n;
            yield return new Vec2(x, seg.Y + amplitude * Math.Sin(2 * Math.PI * (x - seg.X0) / wavelength + phase));
        }
    }

    private static IEnumerable<Vec2> Zigzag(FillGenerator.RowSegment seg, double amplitude, double wavelength, double phase)
    {
        double half = wavelength / 2;
        double sign = phase == 0 ? 1 : -1;
        double x = seg.X0;
        yield return new Vec2(x, seg.Y);
        x += half / 2;
        while (x < seg.X1)
        {
            yield return new Vec2(x, seg.Y + sign * amplitude);
            sign = -sign;
            x += half;
        }
        yield return new Vec2(seg.X1, seg.Y);
    }
}
