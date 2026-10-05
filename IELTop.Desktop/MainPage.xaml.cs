using Microsoft.AspNetCore.Components.WebView.Maui;

namespace IELTop.Desktop;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

        // Blazor needs one root component. It renders nothing, so the page the
        // student sees is still the React app.
        AppBlazorView.RootComponents.Add(new RootComponent
        {
            Selector = "#blazor-app",
            ComponentType = typeof(Web.BlazorHost),
        });
    }
}