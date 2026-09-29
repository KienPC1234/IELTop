using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace IELTop.Styles;

/// <summary>
/// Binds plain text into a RichTextBox document, so passages can use real
/// text selection with highlight. Rebuilds the document when text changes.
/// </summary>
public static class RichText
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(RichText),
            new PropertyMetadata(string.Empty, OnTextChanged));

    public static string GetText(DependencyObject target)
        => (string)target.GetValue(TextProperty);

    public static void SetText(DependencyObject target, string value)
        => target.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is RichTextBox box)
            box.Document = Build(e.NewValue as string ?? string.Empty);
    }

    public static FlowDocument Build(string text)
    {
        // Page background stays white. The foreground is inherited from the
        // RichTextBox, so contrast mode (yellow on black) still applies.
        var document = new FlowDocument { Background = Brushes.Transparent };
        document.Blocks.Add(new Paragraph(new Run(text)));
        return document;
    }
}
