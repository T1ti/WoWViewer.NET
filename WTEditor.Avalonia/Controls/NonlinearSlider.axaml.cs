using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace WTEditor.Avalonia.Controls;

/// <summary>
/// Slider whose UI position is mapped through a power curve. A curve greater
/// than one gives precise control near the minimum while preserving access to
/// a large maximum value near the end of the track.
/// </summary>
public partial class NonlinearSlider : UserControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<NonlinearSlider, double>(
            nameof(Value),
            defaultBindingMode: global::Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<NonlinearSlider, double>(nameof(Minimum), 0d);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<NonlinearSlider, double>(nameof(Maximum), 1d);

    public static readonly StyledProperty<double> CurveProperty =
        AvaloniaProperty.Register<NonlinearSlider, double>(nameof(Curve), 3.5d);

    private bool _updatingSlider;

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Curve
    {
        get => GetValue(CurveProperty);
        set => SetValue(CurveProperty, value);
    }

    public NonlinearSlider()
    {
        InitializeComponent();
        PART_Slider.PropertyChanged += SliderOnPropertyChanged;
        UpdateSliderFromValue();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty ||
            change.Property == MinimumProperty ||
            change.Property == MaximumProperty ||
            change.Property == CurveProperty)
        {
            UpdateSliderFromValue();
        }
    }

    private void SliderOnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (_updatingSlider || change.Property != RangeBase.ValueProperty)
            return;

        SetCurrentValue(ValueProperty, FromNormalized(PART_Slider.Value));
    }

    private void UpdateSliderFromValue()
    {
        if (PART_Slider == null || _updatingSlider)
            return;

        var minimum = Math.Min(Minimum, Maximum);
        var maximum = Math.Max(Minimum, Maximum);
        var clamped = Math.Clamp(Value, minimum, maximum);
        if (Math.Abs(clamped - Value) > double.Epsilon)
            SetCurrentValue(ValueProperty, clamped);

        _updatingSlider = true;
        PART_Slider.Value = ToNormalized(clamped);
        _updatingSlider = false;
    }

    private double ToNormalized(double value)
    {
        var range = Maximum - Minimum;
        if (range <= 0d)
            return 0d;

        var normalized = Math.Clamp((value - Minimum) / range, 0d, 1d);
        return Math.Pow(normalized, 1d / Math.Max(1d, Curve));
    }

    private double FromNormalized(double normalized)
    {
        var minimum = Math.Min(Minimum, Maximum);
        var maximum = Math.Max(Minimum, Maximum);
        var range = maximum - minimum;
        if (range <= 0d)
            return minimum;

        var value = minimum + Math.Pow(Math.Clamp(normalized, 0d, 1d), Math.Max(1d, Curve)) * range;
        return Math.Clamp(value, minimum, maximum);
    }
}
