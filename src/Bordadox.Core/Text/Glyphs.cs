using Bordadox.Core.Geometry;

namespace Bordadox.Core.Text;

/// <summary>
/// Letra pronta para costurar. Unidades em "em" (1 = tamanho da fonte), com a linha de base em Y = 0
/// e Y para baixo (letras acima da linha de base têm Y negativo).
/// </summary>
public sealed record Glyph(double Advance, IReadOnlyList<IReadOnlyList<Vec2>> Contours);

/// <summary>Fornece os contornos das letras de uma fonte. O núcleo não depende de nenhuma biblioteca de fontes.</summary>
public interface IGlyphProvider
{
    /// <summary>Altura das maiúsculas como fração do em (ex.: 0,716 para Arial).</summary>
    double CapHeight(string family, bool bold, bool italic);

    /// <summary>Contornos da letra, ou null se a fonte não tem esse caractere.</summary>
    Glyph? GetGlyph(string family, bool bold, bool italic, int codePoint);
}

public static class GlyphService
{
    /// <summary>Definido pelo aplicativo na inicialização (WPF) ou pelos testes (fonte falsa).</summary>
    public static IGlyphProvider? Provider { get; set; }
}
