using System.Windows;
using Bordadox.App.Text;
using Bordadox.Core.Text;

namespace Bordadox.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        GlyphService.Provider = new WpfGlyphProvider();
        FontCatalog.Start(); // lê as fontes instaladas em segundo plano, sem atrasar a abertura

        // Um erro inesperado não deve derrubar o programa (e levar junto o trabalho não salvo).
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(
                "Ocorreu um erro inesperado:\n\n" + args.Exception.Message +
                "\n\nO programa continua aberto. Se possível, salve o seu trabalho agora (Ctrl+S).",
                "Bordadox", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);
    }
}
