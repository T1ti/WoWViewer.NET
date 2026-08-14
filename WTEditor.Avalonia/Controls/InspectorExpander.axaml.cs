using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace WTEditor.Avalonia.Controls;

public partial class InspectorExpander : UserControl
{
    public static readonly StyledProperty<string> HeaderProperty =
        AvaloniaProperty.Register<InspectorExpander, string>(nameof(Header), string.Empty);
    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<InspectorExpander, bool>(
            nameof(IsExpanded), false, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<object?> SectionContentProperty =
        AvaloniaProperty.Register<InspectorExpander, object?>(nameof(SectionContent));

    public string Header { get => GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public bool IsExpanded { get => GetValue(IsExpandedProperty); set => SetValue(IsExpandedProperty, value); }
    public object? SectionContent { get => GetValue(SectionContentProperty); set => SetValue(SectionContentProperty, value); }

    public InspectorExpander()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == SectionContentProperty || change.Property == DataContextProperty) &&
            SectionContent is Control content)
            content.DataContext = DataContext;
    }
}
