using System.Windows.Input;

namespace Bordadox.App;

/// <summary>
/// Comandos da janela. Os atalhos de tecla única (V, R, C, F, H, 0, 1, +, -, P, Del, Home, End) são tratados em
/// MainWindow.OnPreviewKeyDown, porque o WPF não aceita KeyGesture sem modificador para letras e números.
/// </summary>
public static class AppCommands
{
    public static readonly RoutedUICommand ZoomFit = new("Ajustar à tela", nameof(ZoomFit), typeof(AppCommands));
    public static readonly RoutedUICommand ZoomActual = new("Tamanho real", nameof(ZoomActual), typeof(AppCommands));
    public static readonly RoutedUICommand ZoomIn = new("Aumentar zoom", nameof(ZoomIn), typeof(AppCommands));
    public static readonly RoutedUICommand ZoomOut = new("Diminuir zoom", nameof(ZoomOut), typeof(AppCommands));
    public static readonly RoutedUICommand PlayPause = new("Reproduzir/pausar", nameof(PlayPause), typeof(AppCommands));
    public static readonly RoutedUICommand Export = new("Exportar", nameof(Export), typeof(AppCommands));
    public static readonly RoutedUICommand Duplicate = new("Duplicar", nameof(Duplicate), typeof(AppCommands));
    public static readonly RoutedUICommand Delete = new("Excluir", nameof(Delete), typeof(AppCommands));
    public static readonly RoutedUICommand Digitize = new("Digitalizar imagem", nameof(Digitize), typeof(AppCommands));
}
