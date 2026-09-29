using System;
using System.Windows;
using System.Windows.Threading;

namespace IELTop.Styles;

/// <summary>
/// Brings an element into view when a bound flag turns true. Used so the
/// question the student jumps to is scrolled into sight, both in the reading
/// question list and in the full listening question list.
/// </summary>
public static class BringIntoView
{
    public static readonly DependencyProperty WhenProperty =
        DependencyProperty.RegisterAttached(
            "When", typeof(bool), typeof(BringIntoView),
            new PropertyMetadata(false, OnWhenChanged));

    public static bool GetWhen(DependencyObject target)
        => (bool)target.GetValue(WhenProperty);

    public static void SetWhen(DependencyObject target, bool value)
        => target.SetValue(WhenProperty, value);

    private static void OnWhenChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || target is not FrameworkElement element) return;
        // Defer, so the element is in the scroll host before it is asked to move.
        element.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(element.BringIntoView));
    }
}
