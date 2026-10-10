using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Bordadox.App.Tests;

/// <summary>Garante que a janela Ajuda › Atalhos do teclado não fique para trás dos menus.</summary>
public class ShortcutsTests
{
    [Fact]
    public void Todo_atalho_mostrado_nos_menus_esta_na_lista()
    {
        var xamlPath = Path.GetFullPath(Path.Combine(SourceDir(), "..", "..", "src", "Bordadox.App", "MainWindow.xaml"));
        var gestures = Regex.Matches(File.ReadAllText(xamlPath), "InputGestureText=\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
        Assert.NotEmpty(gestures);

        var listed = Shortcuts.All.Select(e => e.Keys).ToList();
        var missing = gestures.Where(g => !listed.Any(k => k.Contains(g, StringComparison.Ordinal))).ToList();
        Assert.Empty(missing);
    }

    [Fact]
    public void Toda_entrada_tem_grupo_funcao_e_atalho()
    {
        Assert.NotEmpty(Shortcuts.All);
        foreach (var e in Shortcuts.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Group), $"Sem grupo: {e.Function}");
            Assert.False(string.IsNullOrWhiteSpace(e.Function), $"Sem função no grupo {e.Group}");
            Assert.False(string.IsNullOrWhiteSpace(e.Keys), $"Sem atalho: {e.Function}");
        }
    }

    private static string SourceDir([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
