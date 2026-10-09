using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Bordadox.Core.Geometry;
using Bordadox.Core.Text;

namespace Bordadox.App.Text;

/// <summary>Lê os contornos das letras das fontes instaladas no Windows (WPF GlyphTypeface).</summary>
public sealed class WpfGlyphProvider : IGlyphProvider
{
    // Tamanho de referência para extrair o contorno; o resultado é dividido por ele (unidades em "em").
    private const double RefEm = 100;
    // Precisão do achatamento das curvas: 0,2 em 100 = 0,002 em (≈ 0,03 mm numa fonte de 14 mm).
    private const double FlattenTolerance = 0.2;
    private const string FallbackFamily = "Segoe UI";

    private readonly Dictionary<(string, bool, bool), GlyphTypeface?> _faces = new();
    private readonly Dictionary<(string, bool, bool, int), Glyph?> _glyphs = new();
    private readonly Dictionary<(string, bool, bool, int, int), double> _kerning = new();
    private readonly object _lock = new();

    /// <summary>
    /// A fonte tem letras latinas? Fontes de símbolos/ícones (Marlett, Segoe MDL2 Assets...) e só árabes/hebraicas
    /// gerariam texto vazio, então não servem para bordar.
    /// </summary>
    public static bool HasLatinLetters(string family) =>
        Load(family, bold: false, italic: false) is { } face &&
        face.CharacterToGlyphMap.ContainsKey('A') && face.CharacterToGlyphMap.ContainsKey('a');

    public double CapHeight(string family, bool bold, bool italic)
    {
        lock (_lock)
        {
            var face = Face(family, bold, italic);
            return face is { CapsHeight: > 0.2 } ? face.CapsHeight : 0.7;
        }
    }

    public Glyph? GetGlyph(string family, bool bold, bool italic, int codePoint)
    {
        lock (_lock)
        {
            var key = (family, bold, italic, codePoint);
            if (_glyphs.TryGetValue(key, out var cached)) return cached;
            var glyph = Build(Face(family, bold, italic), codePoint);
            _glyphs[key] = glyph;
            return glyph;
        }
    }

    /// <summary>
    /// Kerning de um par: a largura do par menos a soma das larguras das letras sozinhas, medida pelo motor de texto do WPF
    /// (que aplica a tabela de kerning da fonte). Em em. Diferenças menores que 0,002 em são ruído e valem zero.
    /// </summary>
    public double Kerning(string family, bool bold, bool italic, int left, int right)
    {
        lock (_lock)
        {
            var key = (family, bold, italic, left, right);
            if (_kerning.TryGetValue(key, out var cached)) return cached;
            double k = 0;
            try
            {
                var typeface = new Typeface(new FontFamily(family),
                    italic ? FontStyles.Italic : FontStyles.Normal, bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
                double Width(string s) => new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, RefEm,
                    Brushes.Black, 1.0).WidthIncludingTrailingWhitespace;
                string l = char.ConvertFromUtf32(left), r = char.ConvertFromUtf32(right);
                k = (Width(l + r) - Width(l) - Width(r)) / RefEm;
                if (Math.Abs(k) < 0.002 || Math.Abs(k) > 0.4) k = 0;
            }
            catch (Exception) { k = 0; }
            _kerning[key] = k;
            return k;
        }
    }

    private GlyphTypeface? Face(string family, bool bold, bool italic)
    {
        var key = (family, bold, italic);
        if (_faces.TryGetValue(key, out var face)) return face;
        face = Load(family, bold, italic) ?? Load(FallbackFamily, bold, italic);
        _faces[key] = face;
        return face;
    }

    private static GlyphTypeface? Load(string family, bool bold, bool italic)
    {
        try
        {
            var typeface = new Typeface(new FontFamily(family),
                italic ? FontStyles.Italic : FontStyles.Normal,
                bold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);
            return typeface.TryGetGlyphTypeface(out var gt) ? gt : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Glyph? Build(GlyphTypeface? face, int codePoint)
    {
        if (face == null || !face.CharacterToGlyphMap.TryGetValue(codePoint, out var index)) return null;

        double advance = face.AdvanceWidths[index];
        var contours = new List<IReadOnlyList<Vec2>>();

        var outline = face.GetGlyphOutline(index, RefEm, RefEm);
        if (outline != null && !outline.IsEmpty())
        {
            var flat = outline.GetFlattenedPathGeometry(FlattenTolerance, ToleranceType.Absolute);
            foreach (var figure in flat.Figures)
            {
                var pts = new List<Vec2> { new(figure.StartPoint.X / RefEm, figure.StartPoint.Y / RefEm) };
                foreach (var segment in figure.Segments)
                {
                    switch (segment)
                    {
                        case PolyLineSegment poly:
                            pts.AddRange(poly.Points.Select(p => new Vec2(p.X / RefEm, p.Y / RefEm)));
                            break;
                        case LineSegment line:
                            pts.Add(new Vec2(line.Point.X / RefEm, line.Point.Y / RefEm));
                            break;
                    }
                }
                // Remove o ponto final repetido de figuras fechadas.
                if (pts.Count > 1 && Vec2.Distance(pts[0], pts[^1]) < 1e-9) pts.RemoveAt(pts.Count - 1);
                if (pts.Count >= 3) contours.Add(pts);
            }
        }
        return new Glyph(advance, contours);
    }
}
