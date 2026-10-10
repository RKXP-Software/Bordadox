using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using Bordadox.App.Controls;
using Bordadox.App.Editor;
using Bordadox.Core.Design;
using Bordadox.Core.Geometry;

namespace Bordadox.App.Tests;

/// <summary>
/// Testa cada item do menu Objeto pela janela principal: aciona o evento Click do item, como um clique do usuário,
/// e confere o efeito no desenho. Os testes ficam nesta classe, que roda um de cada vez, porque o gancho
/// <see cref="InputDialog.AutoAnswer"/> é compartilhado.
/// </summary>
public class ObjectMenuWindowTests
{
    /// <summary>Todos os itens do menu Objeto (sem separadores). Se um item novo for criado, adicione aqui e escreva o teste.</summary>
    private static readonly string[] ObjetoItems =
    {
        "Girar 90° horário",
        "Girar 90° anti-horário",
        "Girar por ângulo...",
        "Espelhar horizontal",
        "Espelhar vertical",
        "Redimensionar...",
        "Centralizar no bastidor",
        "Converter em aplique",
        "Converter em preenchimento",
        "Explodir (separar em partes)",
        "Costurar antes",
        "Costurar depois",
    };

    [Fact]
    public void Menu_TemOsItensEsperadosESaoTodosTestados()
    {
        OnSta(() =>
        {
            var (w, _) = NewWindow();
            Assert.Equal(ObjetoItems, ObjetoMenu(w).Select(Label).ToList());
        });
    }

    [Fact]
    public void GirarHorario_Gira90GrausNoSentidoDoRelogio()
    {
        OnSta(() =>
        {
            var (w, e) = NewWindow();
            var sq = AddSquare(e, 0, 0, 10);
            var antes = sq.Outline[0].P;
            var angulo = sq.Angle;  // preenchimento nasce com 45°

            Click(w, "Girar 90° horário");

            Assert.Equal(angulo + 90, sq.Angle, 6);
            Assert.False(Near(sq.Outline[0].P, antes), "o contorno deveria ter girado");
            Assert.Equal(10, sq.Bounds().Width, 6);
        });
    }

    [Fact]
    public void GirarAntiHorario_Gira90GrausNoSentidoContrario()
    {
        OnSta(() =>
        {
            var (w, e) = NewWindow();
            var sq = AddSquare(e, 0, 0, 10);
            var angulo = sq.Angle;

            Click(w, "Girar 90° anti-horário");

            Assert.Equal(angulo - 90, sq.Angle, 6);
        });
    }

    [Fact]
    public void GirarPorAngulo_UsaOValorDigitado()
    {
        OnSta(() =>
        {
            var (w, e) = NewWindow();
            var sq = AddSquare(e, 0, 0, 10);
            var angulo = sq.Angle;

            WithAnswer(title => title == "Girar" ? 30 : null, () => Click(w, "Girar por ângulo..."));

            Assert.Equal(angulo + 30, sq.Angle, 6);
        });
    }

    [Fact]
    public void GirarPorAngulo_CancelarNaoMudaNada()
    {
        OnSta(() =>
        {
            var (w, e) = NewWindow();
            var sq = AddSquare(e, 0, 0, 10);
            var angulo = sq.Angle;

            WithAnswer(_ => null, () => Click(w, "Girar por ângulo..."));

            Assert.Equal(angulo, sq.Angle, 6);
        });
    }

    [Fact]
    public void EspelharHorizontal_TrocaOLadoEsquerdoPeloDireito()
    {
        OnSta(() =>
        {
            var (w, e) = NewWindow();
            var sq = AddSquare(e, 0, 0, 10);

            Click(w, "Espelhar horizontal");

            Assert.True(Near(sq.Outline[0].P, new Vec2(10, 0)), $"canto foi para {sq.Outline[0].P}");
        });
    }

    [Fact]
    public void EspelharVertical_TrocaOTopoPelaBase()
    {
        OnSta(() =>
        {
            var (w, e) = NewWindow();
            var sq = AddSquare(e, 0, 0, 10);

            Click(w, "Espelhar vertical");

            Assert.True(Near(sq.Outline[0].P, new Vec2(0, 10)), $"canto foi para {sq.Outline[0].P}");
        });
    }

    [Fact]
    public void Redimensionar_UsaAPorcentagemDigitada()
    {
        OnSta(() =>
        {
            var (w, e) = NewWindow();
            var sq = AddSquare(e, 0, 0, 10);

            WithAnswer(title => title == "Redimensionar" ? 200 : null, () => Click(w, "Redimensionar..."));

            Assert.Equal(20, sq.Bounds().Width, 6);
        });
    }

    [Fact]
    public void Redimensionar_CancelarNaoMudaNada()
    {
        OnSta(() =>
        {
            var (w, e) = NewWindow();
            var sq = AddSquare(e, 0, 0, 10);

            WithAnswer(_ => null, () => Click(w, "Redimensionar..."));

            Assert.Equal(10, sq.Bounds().Width, 6);
        });
    }

    [Fact]
    public void CentralizarNoBastidor_LevaOCentroParaZero()
    {
        OnSta(() =>
        {
            var (w, e) = NewWindow();
            var sq = AddSquare(e, 30, 40, 10);

            Click(w, "Centralizar no bastidor");

            Assert.True(Near(sq.Bounds().Center, new Vec2(0, 0)), $"centro ficou em {sq.Bounds().Center}");
        });
    }

    [Fact]
    public void ConverterEmAplique_E_EmPreenchimento_IdaEVolta()
    {
        OnSta(() =>
        {
            var (w, e) = NewWindow();
            AddSquare(e, 0, 0, 10);

            Click(w, "Converter em aplique");
            Assert.IsType<AppliqueObject>(e.Design.Objects[0]);

            Click(w, "Converter em preenchimento");
            Assert.IsType<FillObject>(e.Design.Objects[0]);
        });
    }

    [Fact]
    public void ExplodirAplique_SeparaEmPartes()
    {
        OnSta(() =>
        {
            var (w, e) = NewWindow();
            AddSquare(e, 0, 0, 10);
            Click(w, "Converter em aplique");
            Assert.Single(e.Design.Objects);

            Click(w, "Explodir (separar em partes)");

            Assert.True(e.Design.Objects.Count > 1, "o aplique deveria ter virado várias partes");
        });
    }

    [Fact]
    public void Explodir_PreenchimentoSimplesAvisaQueNaoHaNadaASeparar()
    {
        OnSta(() =>
        {
            var (w, e) = NewWindow();
            AddSquare(e, 0, 0, 10);

            Click(w, "Explodir (separar em partes)");

            Assert.Single(e.Design.Objects);
            Assert.StartsWith("⚠", HintText(w), StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CosturarAntes_SobeAObjetoNaOrdem()
    {
        OnSta(() =>
        {
            var (w, e) = NewWindow();
            var a = AddSquare(e, 0, 0, 10);
            var b = AddSquare(e, 30, 0, 10);
            e.Select(new EmbObject[] { b });

            Click(w, "Costurar antes");

            Assert.Same(b, e.Design.Objects[0]);
            Assert.Same(a, e.Design.Objects[1]);
        });
    }

    [Fact]
    public void CosturarDepois_DesceOObjetoNaOrdem()
    {
        OnSta(() =>
        {
            var (w, e) = NewWindow();
            var a = AddSquare(e, 0, 0, 10);
            var b = AddSquare(e, 30, 0, 10);
            e.Select(new EmbObject[] { a });

            Click(w, "Costurar depois");

            Assert.Same(b, e.Design.Objects[0]);
            Assert.Same(a, e.Design.Objects[1]);
        });
    }

    // ---------- Apoio ----------

    private static (MainWindow Window, EditorController Editor) NewWindow()
    {
        var w = new MainWindow();
        var editor = (EditorController)typeof(MainWindow)
            .GetField("_editor", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(w)!;
        editor.LoadDesign(new EmbDesign(), null);
        return (w, editor);
    }

    /// <summary>Adiciona um quadrado (preenchimento). AddObjects deixa o objeto novo selecionado, como na janela.</summary>
    private static FillObject AddSquare(EditorController e, double x, double y, double size)
    {
        var f = new FillObject
        {
            Outline = new List<PathNode>
            {
                new(new Vec2(x, y), false),
                new(new Vec2(x + size, y), false),
                new(new Vec2(x + size, y + size), false),
                new(new Vec2(x, y + size), false),
            },
        };
        e.AddObjects(new EmbObject[] { f });
        return f;
    }

    private static string Label(MenuItem m) => (m.Header?.ToString() ?? "").Replace("_", "");

    private static IEnumerable<MenuItem> ObjetoMenu(Window w)
    {
        var menu = Descendants(w).OfType<Menu>().Single();
        var objeto = menu.Items.OfType<MenuItem>().Single(m => Label(m) == "Objeto");
        return objeto.Items.OfType<MenuItem>();
    }

    /// <summary>Aciona o item como um clique: dispara o evento Click que o XAML liga ao handler.</summary>
    private static void Click(Window w, string label)
    {
        var item = ObjetoMenu(w).Single(m => Label(m) == label);
        Assert.True(item.IsEnabled, $"{label} está desabilitado");
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static string HintText(Window w) =>
        ((System.Windows.Controls.TextBlock)typeof(MainWindow)
            .GetField("HintText", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(w)!).Text;

    private static void WithAnswer(Func<string, double?> answer, Action act)
    {
        InputDialog.AutoAnswer = answer;
        try { act(); }
        finally { InputDialog.AutoAnswer = null; }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static bool Near(Vec2 a, Vec2 b) => Vec2.Distance(a, b) < 1e-6;

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
