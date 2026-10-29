using AWV.WebView.Avalonia.Interfaces;
using AWV.WebView.Avalonia.Managers;

namespace AWV.WebView.Avalonia.Hosts;

/// <summary>
/// Linux WebView host using WebKitGTK off-screen rendering.
///
/// Unlike the old GtkPlug/XEmbed implementation this does not embed a foreign
/// native window, so it is independent of whether Avalonia is running on X11
/// or native Wayland.
/// </summary>
internal sealed class LinuxWebViewHost : WebViewHost.IOffscreenWebViewHost, WebViewHost.INavigationHost
{
    private OffscreenWebView? _webView;
    private WebViewNavigationPolicy _policy = new();

    public event Action<WebViewHost.NavigationSnapshot>? NavigationStateChanged;
    public event Action<string, string>? NavigationBlocked;

    public void SetNavigationPolicy(WebViewNavigationPolicy policy) =>
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));

    public void GoBack() { if (_webView != null) GtkManager.GoBack(_webView); }
    public void GoForward() { if (_webView != null) GtkManager.GoForward(_webView); }
    public void Reload() { if (_webView != null) GtkManager.Reload(_webView); }
    public void Stop() { if (_webView != null) GtkManager.Stop(_webView); }

    private void OnNavigationChanged(WebViewHost.NavigationSnapshot state) => NavigationStateChanged?.Invoke(state);
    private void OnNavigationRejected(string url, string reason) => NavigationBlocked?.Invoke(url, reason);

    public event Action<WebViewHost.WebViewFrame>? FrameReady;

    public void Create(int width, int height, string? url)
    {
        if (_webView != null)
            throw new InvalidOperationException("Linux WebView has already been created.");

        _webView = GtkManager.CreateOffscreenWebView(
            width, height, url, _policy, OnNavigationChanged, OnNavigationRejected);
        _webView.FrameReady += OnFrameReady;
    }
    
    public void Focus(bool focusIn)
    {
        if (_webView != null)
            GtkManager.SendFocus(_webView, focusIn);
    }

    public void Resize(int width, int height)
    {
        if (_webView == null)
            return;

        GtkManager.ResizeOffscreenWebView(_webView, width, height);
    }

    public void Navigate(string url)
    {
        if (_webView == null)
            return;

        GtkManager.NavigateOffscreenWebView(_webView, url);
    }

    public void PointerMoved(double x, double y)
    {
        if (_webView != null)
            GtkManager.SendPointerMotion(_webView, x, y);
    }

    public void PointerPressed(double x, double y, WebViewHost.WebViewPointerButton button)
    {
        if (_webView != null)
            GtkManager.SendPointerButton(_webView, x, y, ToGdkButton(button), true);
    }

    public void PointerReleased(double x, double y, WebViewHost.WebViewPointerButton button)
    {
        if (_webView != null)
            GtkManager.SendPointerButton(_webView, x, y, ToGdkButton(button), false);
    }

    public void Scrolled(double x, double y, double deltaX, double deltaY)
    {
        if (_webView != null)
            GtkManager.SendScroll(_webView, x, y, deltaX, deltaY);
    }

    public void KeyEvent(uint gdkKeyval, bool isDown)
    {
        if (_webView != null)
            GtkManager.SendKey(_webView, gdkKeyval, isDown);
    }

    public void TextInput(string text)
    {
        if (_webView != null && !string.IsNullOrEmpty(text))
            GtkManager.SendUnicodeText(_webView, text);
    }

    public void Destroy()
    {
        var webView = _webView;
        if (webView == null)
            return;

        _webView = null;
        webView.FrameReady -= OnFrameReady;

        try
        {
            GtkManager.DestroyOffscreenWebView(webView);
        }
        catch (Exception ex)
        {
            // Shutdown must not take down Avalonia, but still report failure.
            Console.Error.WriteLine($"[AWV GTK] WebView cleanup failed: {ex}");
        }
    }

    private void OnFrameReady(WebViewHost.WebViewFrame frame)
        => FrameReady?.Invoke(frame);

    private static uint ToGdkButton(WebViewHost.WebViewPointerButton button) =>
        button switch
        {
            WebViewHost.WebViewPointerButton.Left => 1u,
            WebViewHost.WebViewPointerButton.Middle => 2u,
            WebViewHost.WebViewPointerButton.Right => 3u,
            _ => 1u
        };
}