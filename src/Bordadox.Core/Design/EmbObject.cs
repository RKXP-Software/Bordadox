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

/// <summary>Objeto editável do desenho. Gera pontos a partir dos parâmetros (digitalização "viva").</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "tipo")]
[JsonDerivedType(typeof(RunObject), "corrido")]
[JsonDerivedType(typeof(SatinObject), "satin")]
[JsonDerivedType(typeof(FillObject), "preenchimento")]
[JsonDerivedType(typeof(StitchBlockObject), "pontos")]
[JsonDerivedType(typeof(TextObject), "texto")]
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

    /// <summary>Contornos achatados para desenhar seleção e testar cliques.</summary>
    public abstract List<List<Vec2>> Outlines();

    /// <summary>Aplica uma transformação geométrica a todos os pontos.</summary>
    public abstract void Transform(Func<Vec2, Vec2> f);

    /// <summary>true se o clique em <paramref name="p"/> (tolerância em mm) atinge o objeto.</summary>
    public virtual bool HitTest(Vec2 p, double tolerance) =>
        Outlines().Any(o => GeometryUtil.DistanceToPolyline(p, o, ClosedPaths) <= tolerance);

    public Rect2 Bounds() => Rect2.FromPoints(Outlines().SelectMany(o => o));

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
