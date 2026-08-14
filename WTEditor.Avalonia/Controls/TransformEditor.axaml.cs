using Avalonia;
using Avalonia.Controls;

namespace WTEditor.Avalonia.Controls;

public partial class TransformEditor : UserControl
{
    public static readonly StyledProperty<double> PositionXProperty =
        AvaloniaProperty.Register<TransformEditor, double>(nameof(PositionX), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> PositionYProperty =
        AvaloniaProperty.Register<TransformEditor, double>(nameof(PositionY), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> PositionZProperty =
        AvaloniaProperty.Register<TransformEditor, double>(nameof(PositionZ), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> RotationXProperty =
        AvaloniaProperty.Register<TransformEditor, double>(nameof(RotationX), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> RotationYProperty =
        AvaloniaProperty.Register<TransformEditor, double>(nameof(RotationY), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> RotationZProperty =
        AvaloniaProperty.Register<TransformEditor, double>(nameof(RotationZ), defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<double> UniformScaleProperty =
        AvaloniaProperty.Register<TransformEditor, double>(nameof(UniformScale), 1d, defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<bool> IsScaleEnabledProperty =
        AvaloniaProperty.Register<TransformEditor, bool>(nameof(IsScaleEnabled), true);
    public static readonly StyledProperty<string> ScaleHelpTextProperty =
        AvaloniaProperty.Register<TransformEditor, string>(nameof(ScaleHelpText), string.Empty);

    public double PositionX { get => GetValue(PositionXProperty); set => SetValue(PositionXProperty, value); }
    public double PositionY { get => GetValue(PositionYProperty); set => SetValue(PositionYProperty, value); }
    public double PositionZ { get => GetValue(PositionZProperty); set => SetValue(PositionZProperty, value); }
    public double RotationX { get => GetValue(RotationXProperty); set => SetValue(RotationXProperty, value); }
    public double RotationY { get => GetValue(RotationYProperty); set => SetValue(RotationYProperty, value); }
    public double RotationZ { get => GetValue(RotationZProperty); set => SetValue(RotationZProperty, value); }
    public double UniformScale { get => GetValue(UniformScaleProperty); set => SetValue(UniformScaleProperty, value); }
    public bool IsScaleEnabled { get => GetValue(IsScaleEnabledProperty); set => SetValue(IsScaleEnabledProperty, value); }
    public string ScaleHelpText { get => GetValue(ScaleHelpTextProperty); set => SetValue(ScaleHelpTextProperty, value); }

    public TransformEditor()
    {
        InitializeComponent();
    }
}
