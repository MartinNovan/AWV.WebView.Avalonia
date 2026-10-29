using System;
using Avalonia;

namespace AWV.WebView.Avalonia.Sample;

/// <summary>Desktop entry point for the AWV navigation API example.</summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .WithInterFont()
        .LogToTrace();
}
