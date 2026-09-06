using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using WTEditor.Avalonia.ViewModels;

namespace WTEditor.Avalonia.Controls;

public partial class ToolSubModeSelector : UserControl
{
    public static readonly StyledProperty<IEnumerable?> ItemsSourceProperty =
        AvaloniaProperty.Register<ToolSubModeSelector, IEnumerable?>(nameof(ItemsSource));

    public static readonly StyledProperty<ToolSubModeViewModel?> SelectedItemProperty =
        AvaloniaProperty.Register<ToolSubModeSelector, ToolSubModeViewModel?>(
            nameof(SelectedItem),
            defaultBindingMode: BindingMode.TwoWay);

    public ToolSubModeSelector() => InitializeComponent();

    public IEnumerable? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public ToolSubModeViewModel? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }
}
