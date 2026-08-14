using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace WTEditor.Avalonia.Controls;

public partial class CompactNumberBox : UserControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CompactNumberBox, double>(
            nameof(Value),
            defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<CompactNumberBox, double>(nameof(Minimum), double.MinValue);
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<CompactNumberBox, double>(nameof(Maximum), double.MaxValue);
    public static readonly StyledProperty<double> IncrementProperty =
        AvaloniaProperty.Register<CompactNumberBox, double>(nameof(Increment), 0.1d);
    public static readonly StyledProperty<string> FormatProperty =
        AvaloniaProperty.Register<CompactNumberBox, string>(nameof(Format), "0.###");

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Increment { get => GetValue(IncrementProperty); set => SetValue(IncrementProperty, value); }
    public string Format { get => GetValue(FormatProperty); set => SetValue(FormatProperty, value); }

    public CompactNumberBox()
    {
        InitializeComponent();
        UpdateText();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == FormatProperty)
            UpdateText();
    }

    private void IncrementButton_OnClick(object? sender, RoutedEventArgs e) => Commit(Value + Increment);
    private void DecrementButton_OnClick(object? sender, RoutedEventArgs e) => Commit(Value - Increment);
    private void ValueTextBox_OnLostFocus(object? sender, RoutedEventArgs e) => CommitText();

    private void ValueTextBox_OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        CommitText();
        e.Handled = true;
    }

    private void CommitText()
    {
        if (double.TryParse(ValueTextBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value))
            Commit(value);
        else
            UpdateText();
    }

    private void Commit(double value)
    {
        SetCurrentValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
        UpdateText();
    }

    private void UpdateText()
    {
        if (ValueTextBox == null)
            return;
        var text = Value.ToString(Format, CultureInfo.CurrentCulture);
        if (ValueTextBox.Text != text && !ValueTextBox.IsKeyboardFocusWithin)
            ValueTextBox.Text = text;
    }
}
