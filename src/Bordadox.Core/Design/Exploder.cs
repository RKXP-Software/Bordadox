using Bordadox.Core.Geometry;

namespace Bordadox.Core.Design;

/// <summary>
/// "Explodir": separa um objeto complexo em partes independentes, que podem ser movidas, giradas, recoloridas ou excluídas sozinhas.
/// Texto vira uma letra por objeto (cada uma continua sendo texto editável); pontos importados (DST, PES...) viram um objeto por trecho
/// contínuo de costura; aplique e degradê viram um objeto de pontos por passo (a parada da máquina é mantida).
/// Objetos simples (corrido, satin, preenchimento comum) não têm o que explodir.
/// </summary>
public static class Exploder
{
    public static bool CanExplode(EmbObject o) => Explode(o).Count > 1;

    /// <summary>As partes do objeto, na ordem de costura, ou uma lista vazia se ele não pode ser explodido.</summary>
    public static List<EmbObject> Explode(EmbObject o)
    {
        switch (o)
        {
            case TextObject text:
            {
                var letters = text.SplitLetters();
                return letters.Count > 1 ? letters.Cast<EmbObject>().ToList() : new();
            }
            case StitchBlockObject block:
                return Runs(block);
        }

        // Aplique, degradê e qualquer objeto que tenha mais de um passo: um bloco de pontos por passo.
        var steps = o.GenerateSteps(null).Where(s => s.Runs.Any(r => r.Count >= 2)).ToList();
        if (steps.Count < 2) return new();
        var names = o is AppliqueObject ? new[] { "guia", "fixação", "acabamento" } : null;
        var baseName = string.IsNullOrWhiteSpace(o.Name) ? o.TypeName : o.Name;
        var parts = new List<EmbObject>();
        for (int i = 0; i < steps.Count; i++)
        {
            string label = names != null && steps.Count == 3 ? names[i] : $"passo {i + 1}";
            parts.Add(new StitchBlockObject
            {
                Name = $"{baseName} — {label}",
                Color = steps[i].Color,
                Runs = steps[i].Runs.Where(r => r.Count >= 2).Select(r => r.ToList()).ToList(),
                StopBefore = steps[i].StopBefore,
            });
        }
        return parts;
    }

    private static List<EmbObject> Runs(StitchBlockObject block)
    {
        var runs = block.Runs.Where(r => r.Count >= 2).ToList();
        if (runs.Count < 2) return new();
        var baseName = string.IsNullOrWhiteSpace(block.Name) ? "Pontos" : block.Name;
        var parts = new List<EmbObject>();
        for (int i = 0; i < runs.Count; i++)
            parts.Add(new StitchBlockObject
            {
                Name = $"{baseName} — trecho {i + 1}",
                Color = block.Color,
                Runs = new() { runs[i].ToList() },
                MaxStitchMm = block.MaxStitchMm,
                MinStitchMm = block.MinStitchMm,
                StopBefore = i == 0 && block.StopBefore,
            });
        return parts;
    }
}
