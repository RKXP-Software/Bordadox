using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Bordadox.App.Controls;

/// <summary>
/// Campo numérico (em mm, por padrão de 0 a 100) para o usuário digitar o valor que quiser: setas e roda do mouse ajustam, o valor
/// só é aplicado ao sair do campo ou com Enter, e é limitado ao intervalo. Aceita vírgula ou ponto.
/// </summary>
public sealed class NumberBox : TextBox
{
    private double _value;

    public double Minimum { get; init; }
    public double Maximum { get; init; } = 100;
    public double Step { get; init; } = 0.1;

    public event EventHandler? ValueChanged;

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, Minimum, Maximum); Text = Format(_value); }
    }

    public NumberBox()
    {
        Width = 64;
        TextAlignment = TextAlignment.Right;
        Padding = new Thickness(3);
        LostKeyboardFocus += (_, _) => Commit();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Commit(); e.Handled = true; }
            else if (e.Key == Key.Up) { Nudge(1); e.Handled = true; }
            else if (e.Key == Key.Down) { Nudge(-1); e.Handled = true; }
        };
        MouseWheel += (_, e) => { if (IsKeyboardFocusWithin) { Nudge(e.Delta > 0 ? 1 : -1); e.Handled = true; } };
    }

    private void Nudge(int dir)
    {
        Commit();
        double v = Math.Round((_value + dir * Step) / Step) * Step;
        Apply(v);
    }

    private void Commit()
    {
        if (TryParse(Text, out var v)) Apply(v);
        else Text = Format(_value);
    }

    private void Apply(double v)
    {
        v = Math.Clamp(v, Minimum, Maximum);
        Text = Format(v);
        if (Math.Abs(v - _value) < 1e-12) return;
        _value = v;
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string Format(double v) => v.ToString("0.###", CultureInfo.CurrentCulture);

    private static bool TryParse(string s, out double v) =>
        double.TryParse(s.Trim().Replace('.', ','), NumberStyles.Float, new CultureInfo("pt-BR"), out v) ||
        double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
