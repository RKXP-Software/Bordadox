using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;

namespace Bordadox.App.Tests;

/// <summary>Confere que cada botão da barra de ferramentas tem uma explicação completa ao passar o mouse.</summary>
public class ToolbarTooltipTests
{
    /// <summary>Ferramenta (nome do botão) e a tecla que o programa realmente usa para ela (ver MainWindow.OnPreviewKeyDown).</summary>
    private static readonly (string Button, string Key)[] Tools =
    {
        ("ToolSelect", "V"),
        ("ToolRun", "R"),
        ("ToolSatin", "C"),
        ("ToolFill", "F"),
        ("ToolApplique", "A"),
        ("ToolHole", "H"),
        ("ToolText", "T"),
    };

    [Fact]
    public void CadaFerramenta_TemExplicacaoComTituloDescricaoEAtalhoCorreto()
    {
        OnSta(() =>
        {
            var w = new MainWindow();
            var botoes = Descendants(w).OfType<RadioButton>().Where(b => b.Name.StartsWith("Tool", StringComparison.Ordinal)).ToList();
            Assert.Equal(Tools.Length, botoes.Count);

            foreach (var (nome, tecla) in Tools)
            {
                var botao = botoes.Single(b => b.Name == nome);
                var dica = Assert.IsType<ToolTip>(botao.ToolTip);
                var painel = Assert.IsType<StackPanel>(dica.Content);
                var textos = painel.Children.OfType<TextBlock>().Select(t => t.Text).ToList();

                Assert.Equal(2, textos.Count);
                Assert.Contains($"({tecla})", textos[0]);
                Assert.True(textos[1].Length >= 80, $"{nome}: explicação curta demais: \"{textos[1]}\"");
            }
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    /// <summary>Roda o teste numa thread STA (exigência do WPF) e repassa a exceção para o xUnit.</summary>
    private static void OnSta(Action act)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { act(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
    }
}
