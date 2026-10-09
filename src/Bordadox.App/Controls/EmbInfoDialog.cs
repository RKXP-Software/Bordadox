using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Bordadox.Core.Formats;

namespace Bordadox.App.Controls;

/// <summary>
/// Explica por que um .emb da Wilcom não abre e mostra a miniatura guardada nele. Oferece três caminhos:
/// abrir o mesmo desenho em DST/PES se ele já existe ao lado; abrir o .emb no programa da Wilcom instalado; ou abrir o Wilcom TrueSizer
/// no navegador. Nos dois últimos a janela fica esperando o usuário salvar o desenho convertido (DST, PES...) e o abre sozinha.
/// </summary>
public sealed class EmbInfoDialog : Window
{
    private const string TrueSizerUrl = "https://wilcom.com/workspace/truesizer";
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

    private readonly string _embPath;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed, FontWeight = FontWeights.SemiBold };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ConvertedFileFinder? _finder;

    /// <summary>Caminho a abrir no lugar do .emb, se o usuário escolheu ou se o arquivo convertido apareceu; null se só fechou.</summary>
    public string? OpenInstead { get; private set; }

    public EmbInfoDialog(Window owner, string embPath, WilcomEmbException info, string? sidecar)
    {
        _embPath = embPath;
        if (owner.IsLoaded) Owner = owner;   // na abertura pela linha de comando a janela principal ainda não foi mostrada
        Title = "Arquivo .emb da Wilcom";
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxWidth = 600;
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
        row.Children.Add(new TextBlock { Text = info.Message, TextWrapping = TextWrapping.Wrap, Width = 420 });
        panel.Children.Add(row);

        if (sidecar != null)
            panel.Children.Add(new TextBlock
            {
                Text = $"Encontrei na mesma pasta o mesmo desenho em outro formato: {Path.GetFileName(sidecar)}.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), FontWeight = FontWeights.SemiBold,
            });

        panel.Children.Add(_status);

        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        Button Make(string text, bool isDefault = false)
        {
            var b = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(8, 4, 0, 0), IsDefault = isDefault };
            buttons.Children.Add(b);
            return b;
        }
        if (sidecar != null)
            Make($"Abrir {Path.GetFileName(sidecar)}", isDefault: true).Click += (_, _) => { OpenInstead = sidecar; DialogResult = true; };

        var wilcom = Make("Abrir no programa da Wilcom");
        wilcom.ToolTip = "Abre o .emb no programa associado a ele (Wilcom EmbroideryStudio, TrueSizer...). Salve lá o desenho como DST ou PES e o Bordadox o abre sozinho.";
        wilcom.Click += (_, _) => OpenInWilcomProgram();

        var web = Make("Abrir o TrueSizer (navegador)");
        web.ToolTip = "Abre o site do Wilcom TrueSizer. Abra o .emb lá, exporte como DST ou PES (o arquivo vai para Downloads) e o Bordadox o abre sozinho.";
        web.Click += (_, _) => OpenTrueSizer();

        var close = Make("Fechar");
        close.IsCancel = true;
        close.IsDefault = sidecar == null;
        close.Click += (_, _) => DialogResult = false;

        _timer.Tick += (_, _) => CheckForConvertedFile();
        Closing += OnClosing;
        Content = panel;
    }

    private void OpenInWilcomProgram()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_embPath) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            ShowStatus("Nenhum programa da Wilcom está associado aos arquivos .emb neste computador. Use o TrueSizer pelo navegador.", waiting: false);
            return;
        }
        StartWaiting();
    }

    private void OpenTrueSizer()
    {
        try
        {
            Process.Start(new ProcessStartInfo(TrueSizerUrl) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            ShowStatus($"Não consegui abrir o navegador. Acesse {TrueSizerUrl}.", waiting: false);
            return;
        }
        StartWaiting();
    }

    private void StartWaiting()
    {
        _finder = new ConvertedFileFinder(_embPath, ConvertedFileFinder.CommonFolders(), DateTime.Now);
        var folders = string.Join(", ", _finder.Folders.Select(f => Path.GetFileName(f.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : f));
        ShowStatus($"Aguardando o desenho convertido: salve-o como DST ou PES com o nome \"{Path.GetFileNameWithoutExtension(_embPath)}\" " +
                   $"(pastas observadas: {folders}). Quando o arquivo aparecer, o Bordadox o abre sozinho.", waiting: true);
        _timer.Start();
    }

    private void ShowStatus(string text, bool waiting)
    {
        _status.Text = (waiting ? "⏳ " : "⚠ ") + text;
        _status.Visibility = Visibility.Visible;
        if (!waiting) _timer.Stop();
    }

    private void CheckForConvertedFile()
    {
        if (_finder?.Poll() is { } path)
        {
            _timer.Stop();
            OpenInstead = path;
            DialogResult = true;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e) => _timer.Stop();
}
