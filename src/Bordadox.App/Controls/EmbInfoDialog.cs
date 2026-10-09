using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Bordadox.Core.Formats;

namespace Bordadox.App.Controls;

/// <summary>
/// Explica por que um .emb da Wilcom não abre e mostra a miniatura guardada nele. Se existe, na mesma pasta, o mesmo desenho
/// exportado em outro formato (DST, PES...), oferece abri-lo.
/// </summary>
public sealed class EmbInfoDialog : Window
{
    private static readonly string[] SidecarExtensions = { "dst", "pes", "exp", "jef", "vp3" };

    /// <summary>Caminho do mesmo desenho em outro formato, ao lado do .emb (mesmo nome), ou null.</summary>
    public static string? FindSidecar(string embPath)
    {
        var dir = Path.GetDirectoryName(embPath);
        if (string.IsNullOrEmpty(dir)) return null;
        var name = Path.GetFileNameWithoutExtension(embPath);
        foreach (var ext in SidecarExtensions)
        {
            var candidate = Directory.EnumerateFiles(dir, name + "." + ext).FirstOrDefault(); // o Windows não diferencia maiúsculas
            if (candidate != null) return candidate;
        }
        return null;
    }

    /// <summary>Caminho a abrir no lugar do .emb, se o usuário escolheu; null se só fechou.</summary>
    public string? OpenInstead { get; private set; }

    public EmbInfoDialog(Window owner, string embPath, WilcomEmbException info, string? sidecar)
    {
        if (owner.IsLoaded) Owner = owner;   // na abertura pela linha de comando a janela principal ainda não foi mostrada
        Title = "Arquivo .emb da Wilcom";
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 560;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = owner.IsLoaded ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = Path.GetFileName(embPath), FontWeight = FontWeights.SemiBold, FontSize = 15, Margin = new Thickness(0, 0, 0, 10) });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (info.PreviewBmp is { } bmp)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = new MemoryStream(bmp);
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                row.Children.Add(new Border
                {
                    BorderBrush = System.Windows.Media.Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Top,
                    Child = new Image { Source = image, Width = 100, Height = 100 },
                });
            }
            catch (Exception) { /* miniatura inválida: segue sem ela */ }
        }
        row.Children.Add(new TextBlock { Text = info.Message, TextWrapping = TextWrapping.Wrap, Width = 380 });
        panel.Children.Add(row);

        if (sidecar != null)
            panel.Children.Add(new TextBlock
            {
                Text = $"Encontrei na mesma pasta o mesmo desenho em outro formato: {Path.GetFileName(sidecar)}.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), FontWeight = FontWeights.SemiBold,
            });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        if (sidecar != null)
        {
            var open = new Button { Content = $"Abrir {Path.GetFileName(sidecar)}", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            open.Click += (_, _) => { OpenInstead = sidecar; DialogResult = true; };
            buttons.Children.Add(open);
        }
        var close = new Button { Content = "Fechar", Padding = new Thickness(14, 5, 14, 5), IsCancel = true, IsDefault = sidecar == null };
        close.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        Content = panel;
    }
}
