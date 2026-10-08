using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MineEditor.ViewModels;
using Windows.System;

namespace MineEditor.Controls;

/// <summary>Chooses the editor template for a field's kind.</summary>
public sealed partial class FieldTemplateSelector : DataTemplateSelector
{
    public DataTemplate? Text { get; set; }

    public DataTemplate? Toggle { get; set; }

    public DataTemplate? Choice { get; set; }

    public DataTemplate? ReadOnly { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => (item as FieldViewModel)?.Kind switch
    {
        FieldKind.Toggle => Toggle,
        FieldKind.Choice => Choice,
        FieldKind.ReadOnly => ReadOnly,
        _ => Text,
    };

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}

/// <summary>Shows groups of editable fields as cards.</summary>
public sealed partial class FieldForm : UserControl
{
    public FieldForm()
    {
        InitializeComponent();
    }

    public IEnumerable<FieldGroup>? Groups
    {
        get => GroupList.ItemsSource as IEnumerable<FieldGroup>;
        set => GroupList.ItemsSource = value;
    }

    public static Visibility VisibleIf(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

    // Enter commits a text field without having to move focus away.
    private void OnTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && sender is TextBox { DataContext: FieldViewModel field } box)
        {
            field.Text = box.Text;
            box.Text = field.Text;
            e.Handled = true;
        }
    }
}
