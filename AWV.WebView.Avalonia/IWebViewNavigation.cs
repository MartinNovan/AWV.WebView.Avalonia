namespace AWV.WebView.Avalonia;

/// <summary>
/// Browser navigation and state without any built-in toolbar. A consumer can
/// bind its own Avalonia UI to <see cref="WebView"/> or call these members.
/// Invoke navigation commands on the Avalonia UI thread.
/// </summary>
public interface IWebViewNavigation
{
    /// <summary>The requested address; settable from XAML.</summary>
    string Url { get; set; }

    /// <summary>Actual document URL, including server redirects and back/forward navigation.</summary>
    string CurrentUrl { get; }

    /// <summary>Whether a previous page is available in the native history.</summary>
    bool CanGoBack { get; }

    /// <summary>Whether a next page is available in the native history.</summary>
    bool CanGoForward { get; }

    /// <summary>Whether the current page is loading.</summary>
    bool IsLoading { get; }

    /// <summary>Live, thread-safe host allow/block rules for page navigation.</summary>
    WebViewNavigationPolicy NavigationPolicy { get; }

    /// <summary>Raised on the Avalonia UI thread whenever one or more navigation properties change.</summary>
    event EventHandler? NavigationStateChanged;

    /// <summary>Raised on the Avalonia UI thread if a URL is rejected by the navigation policy.</summary>
    event EventHandler<WebViewNavigationBlockedEventArgs>? NavigationBlocked;

    /// <summary>Navigate to a URL or local file path.</summary>
    void Navigate(string url);
    /// <summary>Navigate backwards if history exists.</summary>
    void GoBack();
    /// <summary>Navigate forwards if history exists.</summary>
    void GoForward();
    /// <summary>Reload the current page.</summary>
    void Reload();
    /// <summary>Stop loading the current page.</summary>
    void Stop();
}

/// <summary>Details of a blocked top-level navigation.</summary>
public sealed class WebViewNavigationBlockedEventArgs(string url, string reason) : EventArgs
{
    /// <summary>Rejected destination.</summary>
    public string Url { get; } = url;
    /// <summary>Reason for rejection.</summary>
    public string Reason { get; } = reason;
}
