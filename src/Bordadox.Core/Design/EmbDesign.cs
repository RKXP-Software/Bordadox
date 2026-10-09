using System.ComponentModel;
using System.Text.Json;
using Bordadox.Core.Geometry;
using Bordadox.Core.Machine;
using Bordadox.Core.Stitches;

namespace Bordadox.Core.Design;

public sealed class DesignSettings
{
    [Category("Sequência"), DisplayName("Cortar saltos maiores que (mm)"), Range(0, 100, 0.5)]
    public double TrimAtMm { get; set; } = 3;

    [Category("Sequência"), DisplayName("Pontos de arremate")]
    public bool TieStitches { get; set; } = true;

    [Category("Sequência"), DisplayName("Ponto mínimo (mm)"), Range(0, 100, 0.05)]
    public double MinStitchMm { get; set; } = 0.3;

    /// <summary>Com o bastidor deitado, gira o arquivo exportado 90° para caber no bastidor da máquina (que fica em pé).</summary>
    [Category("Exportação"), DisplayName("Girar 90° ao exportar com bastidor deitado")]
    public bool RotateOnExport { get; set; } = true;
}

/// <summary>Imagem de referência para digitalizar por cima. Fica embutida no projeto.</summary>
public sealed class BackgroundImage
{
    private double _widthMm = 100;

    [Browsable(false)]
    public byte[] Data { get; set; } = Array.Empty<byte>();

    [Browsable(false)]
    public string FileName { get; set; } = "";

    /// <summary>Centro da imagem, em mm (0,0 = centro do bastidor).</summary>
    [Category("Imagem de fundo"), DisplayName("Centro X (mm)"), Range(-200, 200, 1)]
    public double X { get; set; }

    [Category("Imagem de fundo"), DisplayName("Centro Y (mm)"), Range(-200, 200, 1)]
    public double Y { get; set; }

    /// <summary>Largura em mm; a altura acompanha mantendo a proporção da imagem.</summary>
    [Category("Imagem de fundo"), DisplayName("Largura (mm)"), Range(0, 1000, 1)]
    public double WidthMm
    {
        get => _widthMm;
        set { _widthMm = value; if (Aspect > 0) HeightMm = value * Aspect; }
    }

    [Browsable(false)]
    public double HeightMm { get; set; } = 100;

    /// <summary>Altura / largura da imagem original.</summary>
    [Browsable(false)]
    public double Aspect { get; set; }

    [Category("Imagem de fundo"), DisplayName("Opacidade"), Range(0.05, 1, 0.05)]
    public double Opacity { get; set; } = 0.5;

    [Category("Imagem de fundo"), DisplayName("Visível")]
    public bool Visible { get; set; } = true;
}

/// <summary>Projeto editável (.bdx): objetos, bastidor, configurações e imagem de fundo.</summary>
public sealed class EmbDesign
{
    public const string Extension = "bdx";

    public int FormatVersion { get; set; } = 1;
    public string HoopName { get; set; } = Hoops.Default.Name;
    /// <summary>true = área de trabalho com o bastidor deitado (largura e altura trocadas).</summary>
    public bool HoopRotated { get; set; }
    public DesignSettings Settings { get; set; } = new();
    public List<EmbObject> Objects { get; set; } = new();
    public BackgroundImage? Background { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, DesignJson.Options);

    public static EmbDesign FromJson(string json) =>
        JsonSerializer.Deserialize<EmbDesign>(json, DesignJson.Options) ?? new EmbDesign();

    public void Save(string path) => File.WriteAllText(path, ToJson());

    public static EmbDesign Load(string path) => FromJson(File.ReadAllText(path));

    /// <summary>Converte um bordado pronto (PES/DST) em um objeto de pontos por bloco de cor.</summary>
    public static EmbDesign FromPattern(StitchPattern pattern)
    {
        var design = new EmbDesign();
        pattern.FixThreadCount();
        int colorIdx = 0;
        foreach (var (color, runs) in pattern.ColorBlocks())
        {
            colorIdx = color;
            var nonEmpty = runs.Where(r => r.Count >= 2).ToList();
            if (nonEmpty.Count == 0) continue;
            design.Objects.Add(new StitchBlockObject
            {
                Name = $"{pattern.Name} — cor {color + 1}",
                Color = pattern.Threads[Math.Min(color, pattern.Threads.Count - 1)],
                Runs = nonEmpty,
            });
        }
        _ = colorIdx;
        var b = design.Bounds();
        if (Hoops.SmallestFittingAnyOrientation(b.Width, b.Height) is { } fit)
        {
            design.HoopName = fit.Hoop.Name;
            design.HoopRotated = fit.Rotated;
        }
        return design;
    }

    public Rect2 Bounds() => Objects.Aggregate(Rect2.Empty, (r, o) => r.Union(o.Bounds()));

    /// <summary>Bastidor escolhido, na orientação da máquina (em pé).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Hoop BaseHoop => Hoops.NQ1400E.FirstOrDefault(h => h.Name == HoopName) ?? Hoops.Default;

    /// <summary>Bastidor como aparece na área de trabalho (deitado, se <see cref="HoopRotated"/>).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Hoop Hoop => HoopRotated ? BaseHoop.Rotated() : BaseHoop;

    public StitchPattern BuildPattern(string name = "Bordadox", StitchCache? cache = null) =>
        Sequencer.Build(this, name, cache);

    /// <summary>
    /// Sequência pronta para gravar no arquivo da máquina. Com o bastidor deitado (e a opção ligada),
    /// gira 90° no sentido horário para o desenho ficar na orientação do bastidor em pé.
    /// </summary>
    public StitchPattern BuildMachinePattern(string name)
    {
        var p = BuildPattern(name);
        return HoopRotated && Settings.RotateOnExport ? p.Rotated90Clockwise() : p;
    }
}
