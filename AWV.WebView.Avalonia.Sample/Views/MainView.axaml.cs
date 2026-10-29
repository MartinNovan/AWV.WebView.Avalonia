using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AWV.WebView.Avalonia.Sample.ViewModels;

namespace AWV.WebView.Avalonia.Sample.Views;

/// <summary>
/// Example browser chrome implemented by the consuming application.
/// AWV only exposes navigation commands/state via IWebViewNavigation;
/// the toolbar, policy editor and status display belong to this view.
/// </summary>
public sealed partial class MainView : UserControl
{
    private const string HomeUrl = "https://example.com";

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public MainView()
    {
        InitializeComponent();

        // Use the public navigation interface; do not access platform hosts.
        IWebViewNavigation browser = AwvWebView;
        browser.NavigationStateChanged += OnNavigationStateChanged;
        browser.NavigationBlocked += OnNavigationBlocked;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncNavigationState();
    }

    private void OnNavigationStateChanged(object? sender, EventArgs e) => SyncNavigationState();

    private void SyncNavigationState()
    {
        if (ViewModel is not { } model)
            return;

        IWebViewNavigation browser = AwvWebView;
        model.CanGoBack = browser.CanGoBack;
        model.CanGoForward = browser.CanGoForward;
        model.IsLoading = browser.IsLoading;
        model.StatusMessage = browser.IsLoading ? "Loading…" : "Ready";

        if (string.IsNullOrWhiteSpace(browser.CurrentUrl))
            return;

        model.CurrentUrl = browser.CurrentUrl;

        // Do not overwrite a URL while the user is typing into the address bar.
        if (!AddressBox.IsFocused)
            model.Address = browser.CurrentUrl;
    }

    private void OnNavigationBlocked(object? sender, WebViewNavigationBlockedEventArgs e)
    {
        if (ViewModel is { } model)
            model.StatusMessage = $"Blocked: {e.Reason} ({e.Url})";
    }

    private void OnBackClicked(object? sender, RoutedEventArgs e) => AwvWebView.GoBack();
    private void OnForwardClicked(object? sender, RoutedEventArgs e) => AwvWebView.GoForward();
    private void OnReloadClicked(object? sender, RoutedEventArgs e) => AwvWebView.Reload();
    private void OnStopClicked(object? sender, RoutedEventArgs e) => AwvWebView.Stop();
    private void OnHomeClicked(object? sender, RoutedEventArgs e) => NavigateTo(HomeUrl);
    private void OnGoClicked(object? sender, RoutedEventArgs e) => NavigateFromAddressBar();

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        NavigateFromAddressBar();
        e.Handled = true;
    }

    private void NavigateFromAddressBar()
    {
        var input = ViewModel?.Address?.Trim();
        if (string.IsNullOrEmpty(input))
            return;

        NavigateTo(input);
    }

    private void NavigateTo(string target)
    {
        // Navigate accepts HTTP(S), file:// URI and absolute/relative file paths.
        // The URL policy is evaluated by AWV, including on link clicks/redirects.
        AwvWebView.Navigate(target);
    }

    private void OnLocalDemoClicked(object? sender, RoutedEventArgs e)
    {
        var file = FindLocalDemoFile();
        if (file is null)
        {
            if (ViewModel is { } model)
                model.StatusMessage = "Cannot find DemoPages/navigation-a.html in the sample source or output directory.";
            return;
        }

        NavigateTo(file);
    }

    /// <summary>
    /// Returns the absolute path to the bundled navigation demo.
    ///
    /// Prefer a copy beside the running EXE. During development the host EXE may
    /// live in Sample.LinuxX11/bin/... rather than in the shared Sample project,
    /// so the source-tree fallback searches parent directories for either sample
    /// layout (the repository-root Sample and samples/Sample).
    /// </summary>
    private static string? FindLocalDemoFile()
    {
        const string fileName = "navigation-a.html";
        const string sharedSampleName = "AWV.WebView.Avalonia.Sample";
        var outputRoot = AppContext.BaseDirectory;

        // For normal published/build output; also support older flat output.
        foreach (var candidate in new[]
        {
            Path.Combine(outputRoot, "DemoPages", fileName),
            Path.Combine(outputRoot, fileName)
        })
        {
            if (File.Exists(candidate))
                return Path.GetFullPath(candidate);
        }

        // Developer fallback for a separate platform launcher project.
        for (DirectoryInfo? directory = new DirectoryInfo(outputRoot);
             directory is not null;
             directory = directory.Parent)
        {
            foreach (var candidate in new[]
            {
                Path.Combine(directory.FullName, "DemoPages", fileName),
                Path.Combine(directory.FullName, sharedSampleName, "DemoPages", fileName),
                Path.Combine(directory.FullName, "samples", sharedSampleName, "DemoPages", fileName)
            })
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private void OnApplyPolicyClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } model)
            return;

        var allowed = ParseHostPatterns(model.AllowedHosts);
        var blocked = ParseHostPatterns(model.BlockedHosts);

        try
        {
            // Validate BOTH lists first to avoid leaving half-applied rules.
            var candidate = new WebViewNavigationPolicy();
            candidate.SetAllowedHosts(allowed);
            candidate.SetBlockedHosts(blocked);

            var policy = AwvWebView.NavigationPolicy;
            policy.SetAllowedHosts(allowed);
            policy.SetBlockedHosts(blocked);
            policy.AllowLocalFiles = model.AllowLocalFiles;

            model.StatusMessage = "Navigation policy updated";
        }
        catch (ArgumentException ex)
        {
            model.StatusMessage = $"Invalid host rule: {ex.Message}";
        }
    }

    private void OnResetPolicyClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } model)
            return;

        AwvWebView.NavigationPolicy.SetAllowedHosts();
        AwvWebView.NavigationPolicy.SetBlockedHosts();
        AwvWebView.NavigationPolicy.AllowLocalFiles = true;
        model.AllowedHosts = string.Empty;
        model.BlockedHosts = string.Empty;
        model.AllowLocalFiles = true;
        model.StatusMessage = "Navigation policy reset";
    }

    private static string[] ParseHostPatterns(string input) =>
        input.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
}
