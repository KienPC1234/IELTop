using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

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
        var document = new FlowDocument();
        document.Blocks.Add(new Paragraph(new Run(text)));
        return document;
    }
}
