using Avalonia.Platform;

namespace AWV.WebView.Avalonia.Interfaces;

/// <summary>
/// Internal contracts implemented by platform-specific browser hosts.
/// </summary>
internal abstract class WebViewHost
{
    public interface IWebViewHost
    {
        IPlatformHandle Create(IPlatformHandle parent, string? url);
        void Destroy(IPlatformHandle control);
    }

    public readonly record struct NavigationSnapshot(
        string CurrentUrl, bool IsLoading, bool CanGoBack, bool CanGoForward);

    /// <summary>
    /// Shared navigation commands used by native and offscreen hosts.
    /// </summary>
    public interface INavigationHost
    {
        event Action<NavigationSnapshot>? NavigationStateChanged;
        event Action<string, string>? NavigationBlocked;
        void SetNavigationPolicy(WebViewNavigationPolicy policy);
        void Navigate(string url);
        void GoBack();
        void GoForward();
        void Reload();
        void Stop();
    }

    public interface IOffscreenWebViewHost
    {
        event Action<WebViewFrame>? FrameReady;
        void Focus(bool focusIn);

        void Create(int width, int height, string? url);
        void Resize(int width, int height);
        void Destroy();

        void PointerMoved(double x, double y);
        void PointerPressed(double x, double y, WebViewPointerButton button);
        void PointerReleased(double x, double y, WebViewPointerButton button);
        void Scrolled(double x, double y, double deltaX, double deltaY);
        void KeyEvent(uint gdkKeyval, bool isDown);

        // For printable characters. Takes already-resolved text (from
        // Avalonia's OnTextInput) instead of a Key enum value, so it reflects
        // whatever keyboard layout is actually active system-wide rather than
        // whatever layout a Key->keyval table was written against.
        void TextInput(string text);
    }

    public readonly record struct WebViewFrame(
        IntPtr Data,
        int Width,
        int Height,
        int Stride);

    public enum WebViewPointerButton
    {
        Left,
        Middle,
        Right
    }
}