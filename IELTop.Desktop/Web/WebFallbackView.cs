using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace IELTop.Desktop.Web;

/// <summary>
/// The screen shown when the web UI cannot be opened. It is a native view on
/// purpose: the web UI is the thing that failed, so it cannot be the thing that
/// explains the failure.
///
/// The browser view is never removed from the window, only covered. Replacing it
/// would take the WebView out of the visual tree, and a retry would then reload
/// a view nobody can see.
/// </summary>
internal static class WebFallbackView
{
    /// <summary>Same tokens as the web UI uses in styles.css.</summary>
    private static readonly Color PageBackground = Color.FromArgb("#ffffff");
    private static readonly Color CardBackground = Color.FromArgb("#f7f7f8");
    private static readonly Color Border = Color.FromArgb("#e2e2e5");
    private static readonly Color Foreground = Color.FromArgb("#18181b");
    private static readonly Color Muted = Color.FromArgb("#71717a");
    private static readonly Color Primary = Color.FromArgb("#18181b");

    /// <summary>
    /// Builds the cover. <paramref name="onRetry"/> is wired to a real control,
    /// and <paramref name="onOpenDownload"/> to a real page, so no button on this
    /// screen is dead.
    /// </summary>
    public static View Create(BrowserProblem problem, Action onRetry, Action<string> onOpenDownload)
    {
        var panel = new VerticalStackLayout { Spacing = 14 };

        panel.Add(new Label
        {
            Text = problem.Title,
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Foreground,
        });

        panel.Add(new Label
        {
            Text = problem.Detail,
            FontSize = 14,
            LineBreakMode = LineBreakMode.WordWrap,
            TextColor = Muted,
        });

        var buttons = new HorizontalStackLayout { Spacing = 10 };

        // A retry only helps when the problem can clear on its own. Asking the
        // student to press it for a missing engine would fail the same way.
        if (problem.CanRetry)
        {
            var retry = NewButton("Try again", Primary);
            retry.Clicked += (_, _) => onRetry();
            buttons.Add(retry);
        }

        if (problem.DownloadUrl is { } url)
        {
            var download = NewButton("Get the browser engine", Primary);
            download.Clicked += (_, _) => onOpenDownload(url);
            buttons.Add(download);
        }

        panel.Add(buttons);

        // Square corners, the same shape as the test windows.
        var card = new Border
        {
            BackgroundColor = CardBackground,
            Stroke = Border,
            StrokeThickness = 1,
            StrokeShape = new Rectangle(),
            MaximumWidthRequest = 560,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = panel,
        };

        return new Grid
        {
            BackgroundColor = PageBackground,
            Padding = new Thickness(24),
            Children = { card },
        };
    }

    private static Button NewButton(string text, Color background) => new()
    {
        Text = text,
        FontSize = 14,
        BackgroundColor = background,
        TextColor = Colors.White,
        CornerRadius = 0,
        HeightRequest = 38,
    };
}