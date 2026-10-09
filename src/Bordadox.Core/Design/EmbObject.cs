using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bordadox.Core.Geometry;
using Bordadox.Core.Stitches;

namespace Bordadox.Core.Design;

/// <summary>Limites e passo de um valor numérico no painel de propriedades.</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RangeAttribute(double min, double max, double step) : Attribute
{
    public double Min { get; } = min;
    public double Max { get; } = max;
    public double Step { get; } = step;
}

/// <summary>Dica de como o painel de propriedades deve editar um texto: "font" (lista de fontes) ou "multiline".</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class EditorHintAttribute(string kind) : Attribute
{
    public string Kind { get; } = kind;
}

/// <summary>Um trecho do bordado de um objeto: sequências de pontos numa cor, com parada opcional da máquina antes (trocar linha, colocar o tecido).</summary>
public sealed record StitchStep(ThreadColor Color, List<List<Vec2>> Runs, bool StopBefore = false);

/// <summary>Objeto editável do desenho. Gera pontos a partir dos parâmetros (digitalização "viva").</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "tipo")]
[JsonDerivedType(typeof(RunObject), "corrido")]
[JsonDerivedType(typeof(SatinObject), "satin")]
[JsonDerivedType(typeof(FillObject), "preenchimento")]
[JsonDerivedType(typeof(StitchBlockObject), "pontos")]
[JsonDerivedType(typeof(TextObject), "texto")]
[JsonDerivedType(typeof(AppliqueObject), "aplique")]
public abstract class EmbObject
{
    [Browsable(false)]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Category("Geral"), DisplayName("Nome")]
    public string Name { get; set; } = "";

    [Browsable(false)]
    public ThreadColor Color { get; set; } = BrotherPalette.Colors[5];

    [Browsable(false), JsonIgnore]
    public abstract string TypeName { get; }

    /// <summary>Caminhos com nós editáveis (alças na tela). Alterações devem ser feitas por <see cref="SetNode"/>.</summary>
    [Browsable(false), JsonIgnore]
    public abstract IReadOnlyList<IReadOnlyList<PathNode>> EditablePaths { get; }

    /// <summary>true se os caminhos editáveis são fechados (formas).</summary>
    [Browsable(false), JsonIgnore]
    public virtual bool ClosedPaths => false;

    public abstract void SetNode(int path, int index, PathNode node);
    public abstract void InsertNode(int path, int index, PathNode node);
    public abstract bool RemoveNode(int path, int index);

    /// <summary>Gera as sequências contínuas de pontos (mm). Entre elas, o sequenciador decide salto/corte.</summary>
    /// <param name="from">Posição atual da agulha, para escolher um início próximo (quando o objeto permite).</param>
    public abstract List<List<Vec2>> GenerateRuns(Vec2? from);

    /// <summary>
    /// Passos do objeto, cada um com a sua cor e, se preciso, uma parada da máquina antes dele. A maioria dos objetos tem um passo só
    /// (as sequências de <see cref="GenerateRuns"/> na cor do objeto); aplique e degradê têm vários.
    /// </summary>
    public virtual List<StitchStep> GenerateSteps(Vec2? from) => new() { new StitchStep(Color, GenerateRuns(from)) };

    /// <summary>Contornos achatados para desenhar seleção e testar cliques.</summary>
    public abstract List<List<Vec2>> Outlines();

    /// <summary>Aplica uma transformação geométrica a todos os pontos.</summary>
    public abstract void Transform(Func<Vec2, Vec2> f);

    /// <summary>true se o clique em <paramref name="p"/> (tolerância em mm) atinge o objeto.</summary>
    public virtual bool HitTest(Vec2 p, double tolerance) =>
        Outlines().Any(o => GeometryUtil.DistanceToPolyline(p, o, ClosedPaths) <= tolerance);

    public Rect2 Bounds() => Rect2.FromPoints(Outlines().SelectMany(o => o));

    // ---------- Tamanho e posição (valem para qualquer objeto, inclusive pontos importados de DST/PES) ----------

    private const double MinBoxMm = 0.1;

    /// <summary>Ao mudar a largura ou a altura, muda a outra na mesma proporção. Só vale na tela; não é gravado no projeto.</summary>
    [Category("Tamanho e posição"), DisplayName("Manter a proporção"), JsonIgnore]
    public bool KeepAspect { get; set; } = true;

    [Category("Tamanho e posição"), DisplayName("Largura total (mm)"), Range(0, 1000, 1), JsonIgnore]
    public double BoxWidthMm
    {
        get => Bounds().Width;
        set => Resize(value, null);
    }

    [Category("Tamanho e posição"), DisplayName("Altura total (mm)"), Range(0, 1000, 1), JsonIgnore]
    public double BoxHeightMm
    {
        get => Bounds().Height;
        set => Resize(null, value);
    }

    [Category("Tamanho e posição"), DisplayName("Centro X (mm)"), Range(-1000, 1000, 1), JsonIgnore]
    public double BoxCenterX
    {
        get => Bounds().IsEmpty ? 0 : Bounds().Center.X;
        set { var b = Bounds(); if (!b.IsEmpty) { double dx = value - b.Center.X; Transform(p => new Vec2(p.X + dx, p.Y)); } }
    }

    [Category("Tamanho e posição"), DisplayName("Centro Y (mm)"), Range(-1000, 1000, 1), JsonIgnore]
    public double BoxCenterY
    {
        get => Bounds().IsEmpty ? 0 : Bounds().Center.Y;
        set { var b = Bounds(); if (!b.IsEmpty) { double dy = value - b.Center.Y; Transform(p => new Vec2(p.X, p.Y + dy)); } }
    }

    private void Resize(double? width, double? height)
    {
        var b = Bounds();
        if (b.IsEmpty) return;
        double sx = 1, sy = 1;
        if (width is { } w) sx = Math.Max(MinBoxMm, w) / Math.Max(1e-9, b.Width);
        if (height is { } h) sy = Math.Max(MinBoxMm, h) / Math.Max(1e-9, b.Height);
        if (KeepAspect) { if (width != null) sy = sx; else sx = sy; }
        if (b.Width < 1e-9) sx = 1;      // uma linha reta vertical não tem largura para escalar
        if (b.Height < 1e-9) sy = 1;
        var c = b.Center;
        Transform(p => new Vec2(c.X + (p.X - c.X) * sx, c.Y + (p.Y - c.Y) * sy));
    }

    public EmbObject Clone()
    {
        var json = JsonSerializer.Serialize(this, DesignJson.Options);
        var copy = JsonSerializer.Deserialize<EmbObject>(json, DesignJson.Options)!;
        copy.Id = Guid.NewGuid();
        return copy;
    }

    protected static void SetIn(List<PathNode> list, int index, PathNode node)
    {
        if (index >= 0 && index < list.Count) list[index] = node;
    }
}

public static class DesignJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        // Mantém acentos legíveis no arquivo .bdx (o padrão escaparia "Círculo" como "Círculo").
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };
}
