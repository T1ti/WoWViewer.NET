using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WTEditor.Avalonia.Controls;

public partial class ColorField : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<ColorField, string>(nameof(Label), string.Empty);
    public static readonly StyledProperty<uint> ValueProperty =
        AvaloniaProperty.Register<ColorField, uint>(nameof(Value));

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public uint Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public ColorField()
    {
        InitializeComponent();
        UpdateColor(Value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty && ColorText != null)
            UpdateColor(change.GetNewValue<uint>());
    }

    private void UpdateColor(uint value)
    {
        ColorText.Text = $"#{value:X8}";
        ColorSwatch.Background = new SolidColorBrush(Color.FromUInt32(value));
    }
}
