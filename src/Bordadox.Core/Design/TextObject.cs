using System.ComponentModel;
using System.Text.Json.Serialization;
using Bordadox.Core.Geometry;
using Bordadox.Core.Text;

namespace Bordadox.Core.Design;

public enum TextAlign
{
    [Description("Esquerda")] Left,
    [Description("Centro")] Center,
    [Description("Direita")] Right,
}

/// <summary>
/// Texto digitado, convertido em preenchimento letra a letra. A posição, a rotação e o espelhamento ficam em
/// três vetores (<see cref="Origin"/>, <see cref="U"/>, <see cref="V"/>): um ponto do texto em (x, y) mm
/// vai para <c>Origin + x·U + y·V</c>. Assim qualquer transformação (mover, girar, espelhar, redimensionar)
/// vira só uma troca desses vetores.
/// </summary>
public sealed class TextObject : EmbObject
{
    /// <summary>Início da linha de base da primeira linha (ou seu centro/fim, conforme o alinhamento).</summary>
    [Browsable(false)]
    public Vec2 Origin { get; set; }

    /// <summary>Direção em que o texto corre (comprimento 1).</summary>
    [Browsable(false)]
    public Vec2 U { get; set; } = new(1, 0);

    /// <summary>Direção "para baixo" do texto (comprimento 1; invertida quando espelhado).</summary>
    [Browsable(false)]
    public Vec2 V { get; set; } = new(0, 1);

    [Category("Texto"), DisplayName("Texto"), EditorHint("multiline")]
    public string Text { get; set; } = "Texto";

    [Category("Texto"), DisplayName("Fonte"), EditorHint("font")]
    public string FontFamily { get; set; } = "Arial";

    [Category("Texto"), DisplayName("Negrito")]
    public bool Bold { get; set; }

    [Category("Texto"), DisplayName("Itálico")]
    public bool Italic { get; set; }

    /// <summary>Altura das letras maiúsculas.</summary>
    [Category("Texto"), DisplayName("Altura das maiúsculas (mm)"), Range(2, 100, 0.5)]
    public double HeightMm { get; set; } = 8;

    [Category("Texto"), DisplayName("Espaço entre letras (mm)"), Range(-2, 10, 0.1)]
    public double LetterSpacing { get; set; }

    [Category("Texto"), DisplayName("Espaço entre linhas (× altura)"), Range(0.8, 3, 0.05)]
    public double LineSpacing { get; set; } = 1.5;

    [Category("Texto"), DisplayName("Alinhamento")]
    public TextAlign Alignment { get; set; } = TextAlign.Left;

    [Category("Texto"), DisplayName("Rotação (graus)"), Range(-180, 180, 5)]
    [JsonIgnore]
    public double RotationDeg
    {
        get => Math.Atan2(U.Y, U.X) * 180 / Math.PI;
        set
        {
            double delta = (value - RotationDeg) * Math.PI / 180;
            U = U.Rotate(delta);
            V = V.Rotate(delta);
        }
    }

    [Category("Preenchimento"), DisplayName("Tipo")]
    public FillType FillType { get; set; } = FillType.Satin;

    [Category("Preenchimento"), DisplayName("Ângulo (graus)"), Range(-180, 180, 5)]
    public double Angle { get; set; } = 45;

    /// <summary>No satin, cada letra usa pontos que cruzam a largura dos seus traços (ignora o ângulo).</summary>
    [Category("Preenchimento"), DisplayName("Satin: ângulo automático (cruza a largura)")]
    public bool SatinAutoAngle { get; set; } = true;

    [Category("Preenchimento"), DisplayName("Densidade (mm entre linhas)"), Range(0.2, 2, 0.05)]
    public double Spacing { get; set; } = 0.4;

    [Category("Preenchimento"), DisplayName("Comprimento do ponto (mm)"), Range(1, 7, 0.1)]
    public double StitchLength { get; set; } = 3.5;

    [Category("Preenchimento"), DisplayName("Compensação de puxada (mm)"), Range(0, 1, 0.05)]
    public double PullCompensation { get; set; } = 0.15;

    [Category("Preenchimento"), DisplayName("Reforço por baixo")]
    public bool Underlay { get; set; } = true;

    public override string TypeName => "Texto";
    public override bool ClosedPaths => true;

    public override IReadOnlyList<IReadOnlyList<PathNode>> EditablePaths => Array.Empty<IReadOnlyList<PathNode>>();
    public override void SetNode(int path, int index, PathNode node) { }
    public override void InsertNode(int path, int index, PathNode node) { }
    public override bool RemoveNode(int path, int index) => false;

    private Vec2 ToWorld(Vec2 p) => Origin + U * p.X + V * p.Y;

    /// <summary>Contornos de cada letra no espaço do texto (mm, sem rotação), na ordem de leitura.</summary>
    public List<List<List<Vec2>>> LocalGlyphs()
    {
        var result = new List<List<List<Vec2>>>();
        var provider = GlyphService.Provider;
        if (provider == null || string.IsNullOrEmpty(Text)) return result;

        double cap = provider.CapHeight(FontFamily, Bold, Italic);
        double em = HeightMm / (cap > 0.2 ? cap : 0.7);
        double pitch = HeightMm * LineSpacing;

        var lines = Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            var glyphs = lines[li].EnumerateRunes()
                .Select(r => provider.GetGlyph(FontFamily, Bold, Italic, r.Value))
                .Where(g => g != null).Select(g => g!).ToList();
            if (glyphs.Count == 0) continue;

            // Posiciona as letras e alinha pela "tinta" (o que se vê), não pelo avanço da fonte:
            // o texto à esquerda começa no ponto clicado e o da direita termina nele.
            double y = li * pitch;
            var placed = new List<List<List<Vec2>>>();
            double x = 0, inkMin = double.PositiveInfinity, inkMax = double.NegativeInfinity;
            foreach (var g in glyphs)
            {
                var contours = g.Contours
                    .Select(c => c.Select(p => new Vec2(x + p.X * em, y + p.Y * em)).ToList())
                    .ToList();
                if (contours.Count > 0)
                {
                    placed.Add(contours);
                    foreach (var p in contours.SelectMany(c => c))
                    {
                        inkMin = Math.Min(inkMin, p.X);
                        inkMax = Math.Max(inkMax, p.X);
                    }
                }
                x += g.Advance * em + LetterSpacing;
            }
            if (placed.Count == 0) continue;

            double shift = Alignment switch
            {
                TextAlign.Center => -(inkMin + inkMax) / 2,
                TextAlign.Right => -inkMax,
                _ => -inkMin,
            };
            foreach (var contours in placed)
                result.Add(contours.Select(c => c.Select(p => new Vec2(p.X + shift, p.Y)).ToList()).ToList());
        }
        return result;
    }

    public override List<List<Vec2>> GenerateRuns(Vec2? from)
    {
        var runs = new List<List<Vec2>>();
        Vec2? localFrom = null;
        foreach (var glyph in LocalGlyphs())
        {
            var contours = glyph.Where(c => c.Count >= 3).ToList();
            if (contours.Count == 0) continue;

            // Cada letra é um preenchimento comum (contornos internos viram furos pela regra par-ímpar).
            var fill = new FillObject
            {
                Outline = contours[0].Select(p => new PathNode(p)).ToList(),
                Holes = contours.Skip(1).Select(c => c.Select(p => new PathNode(p)).ToList()).ToList(),
                FillType = FillType,
                SatinAutoAngle = SatinAutoAngle,
                Angle = Angle,
                Spacing = Spacing,
                StitchLength = StitchLength,
                PullCompensation = PullCompensation,
                Underlay = Underlay,
            };
            var letterRuns = fill.GenerateRuns(localFrom);
            foreach (var r in letterRuns) runs.Add(r.Select(ToWorld).ToList());
            if (letterRuns.Count > 0) localFrom = letterRuns[^1][^1];
        }
        return runs;
    }

    public override List<List<Vec2>> Outlines() =>
        LocalGlyphs().SelectMany(g => g).Select(c => c.Select(ToWorld).ToList()).ToList();

    public override bool HitTest(Vec2 p, double tolerance)
    {
        var outlines = Outlines();
        return GeometryUtil.PointInContours(p, outlines) ||
               outlines.Any(o => GeometryUtil.DistanceToPolyline(p, o, true) <= tolerance);
    }

    public override void Transform(Func<Vec2, Vec2> f)
    {
        var o = f(Origin);
        var u = f(Origin + U) - o;
        var v = f(Origin + V) - o;
        double s = u.Length;
        if (s < 1e-9) return;
        // O fator de escala vai para o tamanho do texto; U e V continuam com comprimento 1.
        HeightMm *= s;
        LetterSpacing *= s;
        Origin = o;
        U = u / s;
        V = v / s;
    }
}
