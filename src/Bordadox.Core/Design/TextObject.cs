using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json.Serialization;
using Bordadox.Core.Generators;
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

    /// <summary>Aproxima pares de letras como "AV" ou "To" usando a tabela de kerning da fonte.</summary>
    [Category("Texto"), DisplayName("Kerning (ajuste entre pares de letras)")]
    public bool Kerning { get; set; } = true;

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

    /// <summary>No satin, os pontos cruzam cada traço da letra perpendicularmente (coluna que segue o traço), em vez de linhas paralelas de um ângulo só.</summary>
    [Category("Preenchimento"), DisplayName("Satin em colunas (segue o traço)")]
    public bool SatinColumns { get; set; } = true;

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

    /// <summary>Uma letra já posicionada: contornos no espaço da própria letra (mm) e o deslocamento até o lugar dela no texto.</summary>
    private sealed record PlacedGlyph(int CodePoint, List<List<Vec2>> Local, Vec2 Offset)
    {
        public List<List<Vec2>> Placed() => Local.Select(c => c.Select(p => p + Offset).ToList()).ToList();
    }

    /// <summary>Posiciona as letras (avanço da fonte, espaço entre letras, kerning e alinhamento pela "tinta").</summary>
    private (List<PlacedGlyph> Glyphs, double Em) Layout()
    {
        var result = new List<PlacedGlyph>();
        var provider = GlyphService.Provider;
        if (provider == null || string.IsNullOrEmpty(Text)) return (result, 0);

        double cap = provider.CapHeight(FontFamily, Bold, Italic);
        double em = HeightMm / (cap > 0.2 ? cap : 0.7);
        double pitch = HeightMm * LineSpacing;

        var lines = Text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            var glyphs = lines[li].EnumerateRunes()
                .Select(r => (Code: r.Value, Glyph: provider.GetGlyph(FontFamily, Bold, Italic, r.Value)))
                .Where(g => g.Glyph != null).Select(g => (g.Code, Glyph: g.Glyph!)).ToList();
            if (glyphs.Count == 0) continue;

            // Posiciona as letras e alinha pela "tinta" (o que se vê), não pelo avanço da fonte:
            // o texto à esquerda começa no ponto clicado e o da direita termina nele.
            double y = li * pitch;
            var placed = new List<(int Code, List<List<Vec2>> Local, double X)>();
            double x = 0, inkMin = double.PositiveInfinity, inkMax = double.NegativeInfinity;
            int? previous = null;
            foreach (var (code, g) in glyphs)
            {
                // Kerning: pares como "AV" ou "To" se encaixam (valor da própria fonte, em em).
                if (Kerning && previous is { } before) x += provider.Kerning(FontFamily, Bold, Italic, before, code) * em;
                var local = g.Contours.Select(c => c.Select(p => new Vec2(p.X * em, p.Y * em)).ToList()).ToList();
                if (local.Count > 0)
                {
                    placed.Add((code, local, x));
                    foreach (var p in local.SelectMany(c => c))
                    {
                        inkMin = Math.Min(inkMin, x + p.X);
                        inkMax = Math.Max(inkMax, x + p.X);
                    }
                }
                x += g.Advance * em + LetterSpacing;
                previous = code;
            }
            if (placed.Count == 0) continue;

            double shift = Alignment switch
            {
                TextAlign.Center => -(inkMin + inkMax) / 2,
                TextAlign.Right => -inkMax,
                _ => -inkMin,
            };
            foreach (var (code, local, gx) in placed) result.Add(new PlacedGlyph(code, local, new Vec2(gx + shift, y)));
        }
        return (result, em);
    }

    /// <summary>Contornos de cada letra no espaço do texto (mm, sem rotação), na ordem de leitura.</summary>
    public List<List<List<Vec2>>> LocalGlyphs() => Layout().Glyphs.Select(g => g.Placed()).ToList();

    // Colunas satin já calculadas por letra (o esqueleto custa alguns milissegundos): mover ou girar o texto não recalcula.
    private sealed record ColumnResult(List<List<(Vec2 A, Vec2 B)>>? Columns);
    private static readonly ConcurrentDictionary<string, ColumnResult> ColumnCache = new();

    /// <summary>Faixas de uma coluna satin mais largas que isto (mm) ficam melhor como preenchimento comum.</summary>
    private const double MaxColumnWidthMm = 6;

    public override List<List<Vec2>> GenerateRuns(Vec2? from)
    {
        var runs = new List<List<Vec2>>();
        Vec2? localFrom = null;
        var (glyphs, em) = Layout();
        foreach (var glyph in glyphs)
        {
            var placed = glyph.Placed();
            var parts = GlyphComponents.Split(glyph.Local);
            var letterRuns = new List<List<Vec2>>();
            if (FillType == FillType.Satin && SatinColumns)
            {
                int partIndex = 0;
                foreach (var part in parts)
                {
                    var columns = CachedColumns(glyph, em, partIndex++, part);
                    var contours = part.Select(c => c.Select(p => p + glyph.Offset).ToList()).ToList();
                    var partRuns = columns != null
                        ? ColumnRuns(columns.Select(c => c.Select(p => (p.A + glyph.Offset, p.B + glyph.Offset)).ToList()).ToList(), contours, localFrom)
                        : FillRuns(contours, localFrom);
                    letterRuns.AddRange(partRuns);
                    if (partRuns.Count > 0) localFrom = partRuns[^1][^1];
                }
            }
            else
            {
                var all = placed.Where(c => c.Count >= 3).ToList();
                if (all.Count == 0) continue;
                letterRuns = FillRuns(all, localFrom);
            }
            foreach (var r in letterRuns) runs.Add(r.Select(ToWorld).ToList());
            if (letterRuns.Count > 0) localFrom = letterRuns[^1][^1];
        }
        return runs;
    }

    private List<List<Vec2>> FillRuns(List<List<Vec2>> contours, Vec2? from)
    {
        if (contours.Count == 0) return new();
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
        return fill.GenerateRuns(from);
    }

    private List<List<(Vec2 A, Vec2 B)>>? CachedColumns(PlacedGlyph glyph, double em, int part, List<List<Vec2>> contours)
    {
        string key = string.Join("|", FontFamily, Bold, Italic, glyph.CodePoint, em.ToString("0.0000"), part, Spacing.ToString("0.###"));
        if (ColumnCache.Count > 4000) ColumnCache.Clear();
        return ColumnCache.GetOrAdd(key, _ => new ColumnResult(SafeBuild(contours, Spacing / 2))).Columns;
    }

    private static List<List<(Vec2 A, Vec2 B)>>? SafeBuild(List<List<Vec2>> contours, double pairSpacing)
    {
        try { return ColumnSatin.Build(contours, pairSpacing, MaxColumnWidthMm); }
        catch (Exception) { return null; }   // letra estranha: cai para o preenchimento comum
    }

    /// <summary>Costura as colunas de uma parte da letra: cada uma com reforço central (se larga) e o satin por cima, ligadas por dentro da letra.</summary>
    private List<List<Vec2>> ColumnRuns(List<List<(Vec2 A, Vec2 B)>> columns, List<List<Vec2>> contours, Vec2? from)
    {
        var settings = new SatinSettings { Spacing = Spacing, PullCompensation = PullCompensation, AutoSplit = true, MaxStitch = 7 };
        var remaining = columns.ToList();
        var runs = new List<List<Vec2>>();
        Vec2? pos = from;
        while (remaining.Count > 0)
        {
            // Próxima coluna: a de ponta mais perto da agulha, começando por essa ponta.
            int best = 0;
            bool reverse = false;
            if (pos is { } p)
            {
                double bestD = double.MaxValue;
                for (int i = 0; i < remaining.Count; i++)
                {
                    var first = Vec2.Lerp(remaining[i][0].A, remaining[i][0].B, 0.5);
                    var last = Vec2.Lerp(remaining[i][^1].A, remaining[i][^1].B, 0.5);
                    if (Vec2.Distance(p, first) < bestD) { bestD = Vec2.Distance(p, first); best = i; reverse = false; }
                    if (Vec2.Distance(p, last) < bestD) { bestD = Vec2.Distance(p, last); best = i; reverse = true; }
                }
            }
            var pairs = remaining[best];
            remaining.RemoveAt(best);
            if (reverse) pairs = Enumerable.Reverse(pairs).ToList();

            var pts = new List<Vec2>();
            double width = pairs.Select(q => Vec2.Distance(q.A, q.B)).OrderBy(w => w).ElementAt(pairs.Count / 2);
            if (Underlay && width >= 1.4)
            {
                // Reforço no meio da coluna na ida; o satin volta por cima.
                pts.AddRange(SatinGenerator.CenterRun(pairs, 2.5));
                pts.AddRange(SatinGenerator.Satin(Enumerable.Reverse(pairs).ToList(), settings));
            }
            else pts.AddRange(SatinGenerator.Satin(pairs, settings));
            if (pts.Count < 2) continue;
            runs.Add(pts);
            pos = pts[^1];
        }
        // Colunas da mesma parte se ligam por dentro dela, sem saltos nem cortes.
        return FillGenerator.ConnectInside(runs, contours);
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
