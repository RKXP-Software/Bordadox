namespace Bordadox.Core.Machine;

/// <summary>Área de bordado útil de um bastidor, em milímetros (largura × altura, com a máquina de frente).</summary>
public sealed record Hoop(string Name, double WidthMm, double HeightMm)
{
    public override string ToString() => Name;

    /// <summary>O mesmo bastidor visto deitado (largura e altura trocadas).</summary>
    public Hoop Rotated() => this with { WidthMm = HeightMm, HeightMm = WidthMm };

    public bool Fits(double widthMm, double heightMm) => widthMm <= WidthMm && heightMm <= HeightMm;
}

public static class Hoops
{
    /// <summary>Bastidores da Brother NQ1400E disponíveis.</summary>
    public static readonly IReadOnlyList<Hoop> NQ1400E = new[]
    {
        new Hoop("Pequeno 100 × 100 mm", 100, 100),
        new Hoop("Médio 130 × 180 mm", 130, 180),
        new Hoop("Grande 160 × 260 mm", 160, 260),
    };

    public static Hoop Default => NQ1400E[^1];

    /// <summary>Menor bastidor que comporta um desenho de <paramref name="widthMm"/> × <paramref name="heightMm"/>.</summary>
    public static Hoop? SmallestFitting(double widthMm, double heightMm) =>
        NQ1400E.FirstOrDefault(h => h.Fits(widthMm, heightMm));

    /// <summary>Menor bastidor que comporta o desenho em pé ou deitado (em pé tem preferência).</summary>
    public static (Hoop Hoop, bool Rotated)? SmallestFittingAnyOrientation(double widthMm, double heightMm)
    {
        foreach (var h in NQ1400E)
        {
            if (h.Fits(widthMm, heightMm)) return (h, false);
            if (h.Rotated().Fits(widthMm, heightMm)) return (h, true);
        }
        return null;
    }
}
