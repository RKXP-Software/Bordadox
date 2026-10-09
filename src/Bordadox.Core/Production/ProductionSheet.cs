using System.Globalization;
using System.Text;
using Bordadox.Core.Design;
using Bordadox.Core.Stitches;

namespace Bordadox.Core.Production;

/// <summary>Uma linha da ficha: a ordem em que a máquina pede cada cor, quantos pontos tem e o que o operador precisa fazer.</summary>
public sealed record SheetColor(int Order, ThreadColor Thread, int Stitches, string Note);

/// <summary>Um objeto do desenho, na ordem de costura.</summary>
public sealed record SheetObject(int Order, string Type, string Name, ThreadColor Color, int Stitches, string Note);

/// <summary>
/// Ficha de produção para imprimir: tamanho, bastidor, número de pontos, cortes, tempo estimado, a sequência de linhas (com as
/// paradas do aplique explicadas) e a lista de objetos. Gera uma página HTML com tudo embutido (a prévia vai como imagem dentro do
/// arquivo), que abre em qualquer navegador e imprime ou salva como PDF.
/// </summary>
public sealed class ProductionSheet
{
    /// <summary>Velocidade usada na estimativa do tempo (pontos por minuto). A NQ1400E chega a 1.000.</summary>
    public const int DefaultSpeed = 700;

    public string Title { get; init; } = "Bordado";
    public string Machine { get; init; } = "Brother NQ1400E";
    public string HoopName { get; init; } = "";
    public bool HoopRotated { get; init; }
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }
    public int Stitches { get; init; }
    public int Trims { get; init; }
    public int ColorChanges { get; init; }
    public int Speed { get; init; } = DefaultSpeed;
    public DateTime Date { get; init; } = DateTime.Now;
    public IReadOnlyList<SheetColor> Colors { get; init; } = Array.Empty<SheetColor>();
    public IReadOnlyList<SheetObject> Objects { get; init; } = Array.Empty<SheetObject>();
    /// <summary>Prévia do bordado (PNG), opcional.</summary>
    public byte[]? PreviewPng { get; init; }

    /// <summary>Minutos de costura: pontos na velocidade informada, mais meio minuto por troca de linha e seis segundos por corte.</summary>
    public double EstimatedMinutes => Stitches / (double)Math.Max(100, Speed) + ColorChanges * 0.5 + Trims * 0.1;

    /// <summary>Monta a ficha a partir da sequência de máquina e, se houver, do desenho (para a lista de objetos e o bastidor).</summary>
    public static ProductionSheet From(StitchPattern pattern, EmbDesign? design = null, string? title = null, byte[]? previewPng = null, int speed = DefaultSpeed)
    {
        pattern.FixThreadCount();
        var b = pattern.Bounds();

        var colors = new List<SheetColor>();
        int block = 0, stitches = 0;
        void Close()
        {
            var thread = pattern.Threads[Math.Min(block, pattern.Threads.Count - 1)];
            string note = "";
            if (block > 0 && SameColor(thread, pattern.Threads[Math.Min(block - 1, pattern.Threads.Count - 1)]))
                note = "Parada com a mesma linha: coloque ou apare o tecido (aplique) e aperte iniciar.";
            colors.Add(new SheetColor(block + 1, thread, stitches, note));
        }
        foreach (var s in pattern.Stitches)
        {
            if (s.Command == StitchCommand.ColorChange)
            {
                Close();
                block++;
                stitches = 0;
            }
            else if (s.Command == StitchCommand.Stitch) stitches++;
        }
        Close();

        var objects = new List<SheetObject>();
        if (design != null)
        {
            var cache = new StitchCache();
            int n = 0;
            foreach (var o in design.Objects)
            {
                var steps = cache.GetSteps(o, null);
                string note = o switch
                {
                    AppliqueObject => "Aplique em 3 passos, com 2 paradas",
                    FillObject { Effect: FillEffect.Gradient } => "Degradê em 2 cores",
                    FillObject { Effect: FillEffect.Contour } => "Segue o contorno",
                    FillObject { Effect: FillEffect.Pattern } => "Padrão decorativo",
                    _ => "",
                };
                objects.Add(new SheetObject(++n, o.TypeName, string.IsNullOrWhiteSpace(o.Name) ? o.TypeName : o.Name, o.Color,
                    steps.Sum(st => st.Runs.Sum(r => r.Count)), note));
            }
        }

        return new ProductionSheet
        {
            Title = title ?? pattern.Name,
            HoopName = design?.Hoop.Name ?? "",
            HoopRotated = design?.HoopRotated ?? false,
            WidthMm = (b.MaxX - b.MinX) / 10.0,
            HeightMm = (b.MaxY - b.MinY) / 10.0,
            Stitches = pattern.StitchCount,
            Trims = pattern.TrimCount,
            ColorChanges = pattern.Stitches.Count(s => s.Command == StitchCommand.ColorChange),
            Colors = colors,
            Objects = objects,
            PreviewPng = previewPng,
            Speed = speed,
        };
    }

    private static bool SameColor(ThreadColor a, ThreadColor b) => a.R == b.R && a.G == b.G && a.B == b.B;

    // ---------- HTML ----------

    private static readonly CultureInfo Pt = new("pt-BR");

    /// <summary>Escapa só o que o HTML exige (o arquivo é UTF-8, então acentos e símbolos ficam legíveis).</summary>
    private static string H(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    private static string Hex(ThreadColor c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public string ToHtml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"pt-BR\"><head><meta charset=\"utf-8\">");
        sb.AppendLine($"<title>Ficha de produção — {H(Title)}</title>");
        sb.AppendLine(Css);
        sb.AppendLine("</head><body>");
        sb.AppendLine($"<h1>Ficha de produção</h1><p class=\"sub\">{H(Title)} · {Date.ToString("dd/MM/yyyy HH:mm", Pt)} · {H(Machine)}</p>");

        sb.AppendLine("<div class=\"top\">");
        if (PreviewPng is { Length: > 0 })
            sb.AppendLine($"<div class=\"preview\"><img alt=\"Prévia do bordado\" src=\"data:image/png;base64,{Convert.ToBase64String(PreviewPng)}\"></div>");
        sb.AppendLine("<table class=\"facts\">");
        void Fact(string k, string v) => sb.AppendLine($"<tr><th>{H(k)}</th><td>{H(v)}</td></tr>");
        Fact("Tamanho", $"{WidthMm.ToString("0.#", Pt)} × {HeightMm.ToString("0.#", Pt)} mm");
        if (HoopName.Length > 0) Fact("Bastidor", HoopName + (HoopRotated ? " (deitado: o arquivo sai girado 90°)" : ""));
        Fact("Pontos", Stitches.ToString("N0", Pt));
        Fact("Cores (linhas)", Colors.Count.ToString(Pt));
        Fact("Trocas de linha / paradas", ColorChanges.ToString(Pt));
        Fact("Cortes", Trims.ToString(Pt));
        Fact("Tempo estimado", $"≈ {Math.Max(1, (int)Math.Round(EstimatedMinutes))} min a {Speed} pontos/min");
        sb.AppendLine("</table></div>");

        sb.AppendLine("<h2>Sequência de linhas</h2>");
        sb.AppendLine("<table class=\"list\"><tr><th>#</th><th>Cor</th><th>Linha</th><th>Pontos</th><th>Observação</th></tr>");
        foreach (var c in Colors)
            sb.AppendLine($"<tr><td>{c.Order}</td><td><span class=\"sw\" style=\"background:{Hex(c.Thread)}\"></span> {Hex(c.Thread)}</td>" +
                          $"<td>{H(c.Thread.Name)}{(string.IsNullOrEmpty(c.Thread.Code) ? "" : " (" + H(c.Thread.Code) + ")")}</td>" +
                          $"<td class=\"n\">{c.Stitches.ToString("N0", Pt)}</td><td>{H(c.Note)}</td></tr>");
        sb.AppendLine("</table>");

        if (Objects.Count > 0)
        {
            sb.AppendLine("<h2>Objetos, na ordem de costura</h2>");
            sb.AppendLine("<table class=\"list\"><tr><th>#</th><th>Tipo</th><th>Nome</th><th>Cor</th><th>Pontos</th><th>Observação</th></tr>");
            foreach (var o in Objects)
                sb.AppendLine($"<tr><td>{o.Order}</td><td>{H(o.Type)}</td><td>{H(o.Name)}</td>" +
                              $"<td><span class=\"sw\" style=\"background:{Hex(o.Color)}\"></span> {H(o.Color.Name)}</td>" +
                              $"<td class=\"n\">{o.Stitches.ToString("N0", Pt)}</td><td>{H(o.Note)}</td></tr>");
            sb.AppendLine("</table>");
        }

        if (Colors.Any(c => c.Note.Length > 0))
            sb.AppendLine("<p class=\"note\"><b>Aplique:</b> a máquina para nas linhas marcadas. Na primeira parada coloque o tecido sobre a linha de posicionamento; " +
                          "depois da fixação, apare o tecido rente à costura e aperte iniciar para o acabamento.</p>");

        sb.AppendLine("<p class=\"foot\">Gerado pelo Bordadox. O tempo é uma estimativa e varia com a máquina, a velocidade e o tecido.</p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private const string Css = """
        <style>
        body { font-family: "Segoe UI", Arial, sans-serif; margin: 24px; color: #222; }
        h1 { margin: 0; font-size: 22px; } h2 { font-size: 15px; margin: 22px 0 6px; border-bottom: 1px solid #ccc; padding-bottom: 3px; }
        .sub { margin: 2px 0 14px; color: #555; }
        .top { display: flex; gap: 20px; align-items: flex-start; }
        .preview { border: 1px solid #ccc; padding: 6px; background: #fff; } .preview img { display: block; max-width: 320px; max-height: 320px; }
        table { border-collapse: collapse; } .facts th { text-align: left; padding: 3px 14px 3px 0; color: #555; font-weight: 600; } .facts td { padding: 3px 0; }
        .list { width: 100%; } .list th { text-align: left; background: #f0f0f0; padding: 4px 8px; font-size: 12px; }
        .list td { padding: 4px 8px; border-bottom: 1px solid #e4e4e4; font-size: 13px; } .n { text-align: right; }
        .sw { display: inline-block; width: 13px; height: 13px; border-radius: 7px; border: 1px solid #888; vertical-align: -2px; }
        .note { margin-top: 14px; padding: 8px 10px; background: #fff8e1; border: 1px solid #f0d98a; font-size: 13px; }
        .foot { margin-top: 24px; color: #888; font-size: 11px; }
        @media print { body { margin: 10mm; } }
        </style>
        """;
}
