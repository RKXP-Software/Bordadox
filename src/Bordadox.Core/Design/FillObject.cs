using System.ComponentModel;
using Bordadox.Core.Generators;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.Core.Design;

public enum FillEffect
{
    [Description("Nenhum")] None,
    [Description("Degradê (duas cores)")] Gradient,
    [Description("Segue o contorno")] Contour,
    [Description("Padrão decorativo")] Pattern,
}

/// <summary>Preenchimento de uma forma fechada, com furos opcionais.</summary>
public sealed class FillObject : ShapeObject
{
    [Category("Preenchimento"), DisplayName("Tipo")]
    public FillType FillType { get; set; } = FillType.Tatami;

    [Category("Preenchimento"), DisplayName("Ângulo (graus)"), Range(-180, 180, 5)]
    public double Angle { get; set; } = 45;

    /// <summary>No satin, ignora o ângulo e faz os pontos cruzarem a largura da forma (como uma coluna satin).</summary>
    [Category("Preenchimento"), DisplayName("Satin: ângulo automático (cruza a largura)")]
    public bool SatinAutoAngle { get; set; } = true;

    [Category("Preenchimento"), DisplayName("Densidade (mm entre linhas)"), Range(0.2, 2, 0.05)]
    public double Spacing { get; set; } = 0.4;

    [Category("Preenchimento"), DisplayName("Comprimento do ponto (mm)"), Range(1, 7, 0.1)]
    public double StitchLength { get; set; } = 3.5;

    [Category("Preenchimento"), DisplayName("Compensação de puxada (mm)"), Range(0, 1, 0.05)]
    public double PullCompensation { get; set; } = 0.2;

    [Category("Preenchimento"), DisplayName("Ponto satin máximo (mm)"), Range(3, 12, 0.5)]
    public double MaxSatinLength { get; set; } = 7;

    [Category("Reforço por baixo"), DisplayName("Tatami por baixo")]
    public bool Underlay { get; set; } = true;

    [Category("Reforço por baixo"), DisplayName("Espaçamento do reforço (mm)"), Range(1, 6, 0.1)]
    public double UnderlaySpacing { get; set; } = 2.5;

    [Category("Reforço por baixo"), DisplayName("Corrido no contorno")]
    public bool EdgeUnderlay { get; set; }

    [Category("Efeito"), DisplayName("Efeito")]
    public FillEffect Effect { get; set; } = FillEffect.None;

    /// <summary>Degradê: a cor do objeto vai de um lado da forma e esta segunda cor do outro, em linhas entremeadas (mais de uma cor no meio).</summary>
    [Category("Efeito"), DisplayName("Degradê: segunda cor")]
    public ThreadColor Color2 { get; set; } = BrotherPalette.Colors[13];

    [Category("Efeito"), DisplayName("Degradê: inverter o sentido")]
    public bool GradientReverse { get; set; }

    [Category("Efeito"), DisplayName("Padrão")]
    public FillPattern Pattern { get; set; } = FillPattern.Wave;

    [Category("Efeito"), DisplayName("Padrão: tamanho (mm)"), Range(1.5, 8, 0.25)]
    public double PatternSize { get; set; } = 3;

    public override string TypeName => "Preenchimento";

    public override List<List<Vec2>> GenerateRuns(Vec2? from) => GenerateSteps(from).SelectMany(s => s.Runs).ToList();

    public override List<StitchStep> GenerateSteps(Vec2? from)
    {
        if (Outline.Count < 3) return new();
        var contours = Contours();
        return Effect switch
        {
            FillEffect.Gradient => GradientSteps(contours, from),
            FillEffect.Contour => One(FillGenerator.ConnectInside(ContourFill.Generate(contours, Spacing, StitchLength, from), contours)),
            FillEffect.Pattern => One(PatternFill.Generate(contours, Pattern, PatternSize, Angle, StitchLength, from)),
            _ => One(PlainRuns(contours, from)),
        };
    }

    private List<StitchStep> One(List<List<Vec2>> runs) => new() { new StitchStep(Color, runs) };

    /// <summary>Tatami ou satin comum, com reforço por baixo.</summary>
    private List<List<Vec2>> PlainRuns(List<IReadOnlyList<Vec2>> contours, Vec2? from)
    {
        var runs = new List<List<Vec2>>();

        // Satin com ângulo automático: os pontos cruzam o lado estreito da forma (paralelos entre si, de borda a borda).
        double angle = FillType == FillType.Satin && SatinAutoAngle
            ? GeometryUtil.MainAxisDegrees(contours[0]) + 90
            : Angle;

        if (EdgeUnderlay)
        {
            foreach (var c in contours)
            {
                var closed = c.Append(c[0]).ToList();
                runs.Add(RunGenerator.ResampleSection(closed, 2.5));
            }
            from = runs[^1][^1];
        }
        if (Underlay)
        {
            runs.AddRange(FillGenerator.Fill(contours, new FillSettings
            {
                AngleDeg = angle + 90,
                Spacing = UnderlaySpacing,
                StitchLength = 3,
                Inset = 0.5,
            }, from));
            if (runs.Count > 0) from = runs[^1][^1];
        }
        runs.AddRange(FillGenerator.Fill(contours, new FillSettings
        {
            AngleDeg = angle,
            Spacing = Spacing,
            StitchLength = StitchLength,
            Satin = FillType == FillType.Satin,
            MaxSatinLength = MaxSatinLength,
            PullCompensation = PullCompensation,
        }, from));
        // Trechos da mesma forma se ligam por dentro dela, sem saltos nem cortes.
        return FillGenerator.ConnectInside(runs, contours);
    }

    /// <summary>
    /// Degradê: as linhas do tatami (a mesma grade de um tatami comum) são divididas entre as duas cores. No começo da forma quase
    /// todas são da primeira cor; no fim, quase todas da segunda; no meio, alternam. A divisão usa difusão de erro, então a
    /// proporção muda de modo contínuo. O sentido vai perpendicular às linhas (gire o ângulo para girar o degradê).
    /// </summary>
    private List<StitchStep> GradientSteps(List<IReadOnlyList<Vec2>> contours, Vec2? from)
    {
        var rows = FillGenerator.Rows(contours, Angle, Spacing);
        if (rows.Count == 0) return new();
        int last = rows.Max(r => r.Row);

        var isSecond = new bool[last + 1];
        double acc = 0.5;                                      // começa no meio: a primeira linha fica com a primeira cor
        for (int r = 0; r <= last; r++)
        {
            double t = last == 0 ? 0 : (double)r / last;
            acc += t;
            if (acc >= 1) { isSecond[r] = true; acc -= 1; }
        }
        // Invertido: o degradê começa na segunda cor e termina na primeira (só trocam as cores, a divisão das linhas é a mesma).
        var first = GradientReverse ? Color2 : Color;
        var second = GradientReverse ? Color : Color2;

        var steps = new List<StitchStep>();
        var layerA = new List<List<Vec2>>();
        if (Underlay)
        {
            layerA.AddRange(FillGenerator.Fill(contours, new FillSettings { AngleDeg = Angle + 90, Spacing = UnderlaySpacing, StitchLength = 3, Inset = 0.5 }, from));
            if (layerA.Count > 0) from = layerA[^1][^1];
        }
        FillSettings Layer(Func<int, bool> keep) => new()
        {
            AngleDeg = Angle,
            Spacing = Spacing,
            StitchLength = StitchLength,
            PullCompensation = PullCompensation,
            KeepRow = keep,
        };
        layerA.AddRange(FillGenerator.Fill(contours, Layer(r => r < isSecond.Length && !isSecond[r]), from));
        steps.Add(new StitchStep(first, FillGenerator.ConnectInside(layerA, contours)));

        var layerB = FillGenerator.Fill(contours, Layer(r => r < isSecond.Length && isSecond[r]), layerA.Count > 0 ? layerA[^1][^1] : from);
        steps.Add(new StitchStep(second, FillGenerator.ConnectInside(layerB, contours)));
        return steps;
    }
}
