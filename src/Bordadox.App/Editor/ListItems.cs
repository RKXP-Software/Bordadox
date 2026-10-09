using System.Windows.Media;
using Bordadox.Core.Design;
using Bordadox.Core.Stitches;

namespace Bordadox.App.Editor;

internal static class Brushes2
{
    public static SolidColorBrush Of(ThreadColor t)
    {
        var b = new SolidColorBrush(Color.FromRgb(t.R, t.G, t.B));
        b.Freeze();
        return b;
    }
}

/// <summary>Linha da lista de sequência de costura.</summary>
public sealed class ObjectItem(EmbObject obj, int index, int stitches)
{
    public EmbObject Object { get; } = obj;
    public string Name => string.IsNullOrWhiteSpace(Object.Name) ? Object.TypeName : Object.Name;
    public string Type => $"{index}. {Object.TypeName} · {Object.Color.Name}";
    public string Detail => $"{stitches:N0} pts";
    public SolidColorBrush Brush { get; } = Brushes2.Of(obj.Color);
}

/// <summary>Cor da paleta Brother.</summary>
public sealed class PaletteItem(ThreadColor thread)
{
    public ThreadColor Thread { get; } = thread;
    public SolidColorBrush Brush { get; } = Brushes2.Of(thread);
    public string Label => $"{Thread.Code} — {Thread.Name}";
}
