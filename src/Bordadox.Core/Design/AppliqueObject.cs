using System.ComponentModel;
using Bordadox.Core.Generators;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.Core.Design;

public enum TackDownType
{
    [Description("Zigue-zague")] Zigzag,
    [Description("Ponto corrido")] Running,
}

/// <summary>
/// Aplique: o tecido é colocado sobre o bordado e preso por costura. Três passos, com parada da máquina entre eles:
/// 1) linha de posicionamento (mostra onde colocar o tecido); 2) fixação (prende o tecido); 3) acabamento em satin sobre a borda.
/// A cor do objeto é a do acabamento; guia e fixação usam a "cor da guia". Entre os passos a máquina para: o usuário coloca o
/// tecido, aperta iniciar, e na parada seguinte apara o tecido que sobrou rente à fixação.
/// </summary>
public sealed class AppliqueObject : ShapeObject
{
    [Category("Aplique"), DisplayName("Linha de posicionamento")]
    public bool PlacementLine { get; set; } = true;

    [Category("Aplique"), DisplayName("Fixação do tecido")]
    public TackDownType TackDown { get; set; } = TackDownType.Zigzag;

    [Category("Aplique"), DisplayName("Cor da guia e da fixação")]
    public ThreadColor GuideColor { get; set; } = BrotherPalette.Colors[39];

    [Category("Acabamento"), DisplayName("Largura do satin (mm)"), Range(0, 100, 0.25)]
    public double CoverWidth { get; set; } = 3;

    [Category("Acabamento"), DisplayName("Densidade (mm entre linhas)"), Range(0, 100, 0.05)]
    public double Spacing { get; set; } = 0.4;

    [Category("Acabamento"), DisplayName("Compensação de puxada (mm)"), Range(0, 100, 0.05)]
    public double PullCompensation { get; set; } = 0.15;

    public override string TypeName => "Aplique";

    public override List<StitchStep> GenerateSteps(Vec2? from)
    {
        var steps = new List<StitchStep>();
        if (Outline.Count < 3) return steps;
        var contours = Contours().Where(c => c.Count >= 3).ToList();

        if (PlacementLine)
            steps.Add(new StitchStep(GuideColor, contours.Select(c => Loop(c, 2.5)).ToList()));

        // O tecido é colocado depois da guia e preso antes do acabamento: as duas paradas valem mesmo se as cores forem iguais.
        var tack = contours.Select(c => TackDown == TackDownType.Zigzag
            ? OutlineSatin.Zigzag(c, 1.4, 1.6)
            : Loop(c, 2.0)).ToList();
        steps.Add(new StitchStep(GuideColor, tack, StopBefore: true));

        var settings = new SatinSettings { Spacing = Math.Max(0.05, Spacing), PullCompensation = PullCompensation, AutoSplit = true, MaxStitch = 7 };
        var cover = new List<List<Vec2>>();
        foreach (var c in contours)
        {
            var pairs = OutlineSatin.Pairs(c, CoverWidth, Spacing / 2);
            if (pairs.Count < 4) continue;
            var pts = SatinGenerator.Satin(pairs, settings);
            if (pts.Count > 0) pts.Add(pts[0]);   // fecha o anel
            cover.Add(pts);
        }
        steps.Add(new StitchStep(Color, cover, StopBefore: true));
        return steps;
    }

    public override List<List<Vec2>> GenerateRuns(Vec2? from) => GenerateSteps(from).SelectMany(s => s.Runs).ToList();

    private static List<Vec2> Loop(IReadOnlyList<Vec2> contour, double stitchLength) =>
        RunGenerator.ResampleSection(contour.Append(contour[0]).ToList(), stitchLength);
}
