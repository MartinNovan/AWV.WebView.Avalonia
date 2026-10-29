#if WINDOWS
using Avalonia.Platform;
using AWV.WebView.Avalonia.Interfaces;
using Microsoft.Web.WebView2.WinForms;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace AWV.WebView.Avalonia.Hosts;

// Windows implementation of WebView using WebView2 + WinForms panel.
// Hosts WebView2 inside Avalonia HWND via reparenting + window subclassing for resize.
internal sealed class WindowsWebViewHost : WebViewHost.IWebViewHost, WebViewHost.INavigationHost
{
    private WebViewNavigationPolicy _policy = new();
    private string _pendingUrl = "about:blank";
    private bool _loading;
    private bool _destroyed;

    public event Action<WebViewHost.NavigationSnapshot>? NavigationStateChanged;
    public event Action<string, string>? NavigationBlocked;

    public void SetNavigationPolicy(WebViewNavigationPolicy policy) =>
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));

    public void Navigate(string url)
    {
        if (!_policy.IsAllowed(url, out var reason))
        {
            NavigationBlocked?.Invoke(url, reason);
            return;
        }
        _pendingUrl = url;
        if (_webView?.CoreWebView2 is { } core)
            core.Navigate(url);
    }

    public void GoBack() { if (_webView?.CoreWebView2?.CanGoBack == true) _webView.CoreWebView2.GoBack(); }
    public void GoForward() { if (_webView?.CoreWebView2?.CanGoForward == true) _webView.CoreWebView2.GoForward(); }
    public void Reload() => _webView?.CoreWebView2?.Reload();
    public void Stop()
    {
        _webView?.CoreWebView2?.Stop();
        _loading = false;
        PublishNavigationState();
    }

    private void PublishNavigationState()
    {
        if (_destroyed) return;
        var core = _webView?.CoreWebView2;
        NavigationStateChanged?.Invoke(new WebViewHost.NavigationSnapshot(
            core?.Source ?? string.Empty, _loading,
            core?.CanGoBack ?? false, core?.CanGoForward ?? false));
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!_policy.IsAllowed(args.Uri, out var reason))
        {
            args.Cancel = true; // Also runs for HTTP redirects.
            NavigationBlocked?.Invoke(args.Uri, reason);
            return;
        }
        _loading = true;
        PublishNavigationState();
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        _loading = false;
        PublishNavigationState();
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        // Opening the system browser would bypass this WebView's host policy.
        args.Handled = true;
        NavigationBlocked?.Invoke(args.Uri, "New windows are not supported");
    }

    private WebView2? _webView;             // the actual WebView2 control
    private Control? _hostPanel;            // WinForms panel to host WebView2
    private IntPtr _parentHwnd;             // Avalonia native HWND
    private IntPtr _prevParentWndProc = IntPtr.Zero; // original parent WndProc
    private bool _isSubclassed;             // flag if parent is subclassed
    private GCHandle? _wndProcHandle;       // keeps delegate alive for SetWindowLongPtr

    // Creates WinForms host + WebView2 and parents it to Avalonia HWND.
    public IPlatformHandle Create(IPlatformHandle parent, string? url)
    {
        _destroyed = false;
        _parentHwnd = parent.Handle;
        _pendingUrl = string.IsNullOrWhiteSpace(url) ? "about:blank" : url;

        // create non-top-level host panel and fill parent
        _hostPanel = new Panel { Dock = DockStyle.Fill };
        _hostPanel.CreateControl();

        // parent it to Avalonia HWND
        SetParent(_hostPanel.Handle, _parentHwnd);

        // set WS_CHILD | WS_VISIBLE so panel shows inside parent
        var style = GetWindowLongPtr(_hostPanel.Handle, GwlStyle);
        style |= WsChild | WsVisible;
        SetWindowLongPtr(_hostPanel.Handle, GwlStyle, style);

        // match panel size to parent
        ResizeHostToParent();

        // subclass parent to track WM_SIZE for automatic resize
        SubclassParentWindow();

        // create WebView2 inside host panel
        _webView = new WebView2 { Dock = DockStyle.Fill };
        _hostPanel.Controls.Add(_webView);
        _webView.CreateControl();

        // initialize WebView2 and navigate to URL
        InitializeWebView();

        // return WebView2 HWND to Avalonia
        return new PlatformHandle(_webView.Handle, "HWND");
    }

    // Clean up native resources when Avalonia destroys the control.
    public void Destroy(IPlatformHandle control)
    {
        _destroyed = true;
        var core = _webView?.CoreWebView2;
        if (core != null)
        {
            core.NavigationStarting -= OnNavigationStarting;
            core.NavigationCompleted -= OnNavigationCompleted;
            core.NewWindowRequested -= OnNewWindowRequested;
        }
        UnsubclassParentWindow(); // restore parent WndProc

        try
        {
            _webView?.Dispose();
            _hostPanel?.Dispose();
        }
        catch
        {
            // ignore exceptions during cleanup
        }

        _webView = null;
        _hostPanel = null;
    }

    // async initialization for WebView2 CoreWebView2 environment
    private async void InitializeWebView()
    {
        var view = _webView;
        if (view == null) return;
        try
        {
            await view.EnsureCoreWebView2Async();
            if (_destroyed || _webView != view) return;

            var core = view.CoreWebView2;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.SourceChanged += (_, _) => PublishNavigationState();
            core.HistoryChanged += (_, _) => PublishNavigationState();
            core.NewWindowRequested += OnNewWindowRequested;
            Navigate(_pendingUrl);
            PublishNavigationState();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"WebView2 initialization failed: {ex}");
        }
    }

    #region Parent subclass + resize

    // adjust host panel to fill Avalonia HWND
    private void ResizeHostToParent()
    {
        if (_hostPanel == null || _parentHwnd == IntPtr.Zero) return;

        if (GetClientRect(_parentHwnd, out Rect rc))
        {
            int w = rc.Right - rc.Left;
            int h = rc.Bottom - rc.Top;
            MoveWindow(_hostPanel.Handle, 0, 0, Math.Max(0, w), Math.Max(0, h), true);
        }
    }

    // subclass parent HWND to intercept size/move messages
    private void SubclassParentWindow()
    {
        if (_isSubclassed || _parentHwnd == IntPtr.Zero) return;

        WndProcDelegate newProc = ParentWndProc;
        _wndProcHandle = GCHandle.Alloc(newProc); // keep delegate alive
        IntPtr ptr = Marshal.GetFunctionPointerForDelegate(newProc);

        _prevParentWndProc = SetWindowLongPtr(_parentHwnd, GwlWndproc, ptr);
        _isSubclassed = true;
    }

    // remove subclass and restore original WndProc
    private void UnsubclassParentWindow()
    {
        if (!_isSubclassed || _parentHwnd == IntPtr.Zero) return;

        SetWindowLongPtr(_parentHwnd, GwlWndproc, _prevParentWndProc);
        _prevParentWndProc = IntPtr.Zero;

        if (_wndProcHandle is { IsAllocated: true })
            _wndProcHandle.Value.Free();

        _isSubclassed = false;
    }

    // intercept parent window messages to handle resizing
    private IntPtr ParentWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint wmSize = 0x0005;
        const uint wmMove = 0x0003;
        const uint wmWindowposchanged = 0x0047;

        if (msg == wmSize || msg == wmMove || msg == wmWindowposchanged)
            ResizeHostToParent();

        return CallWindowProc(_prevParentWndProc, hwnd, msg, wParam, lParam);
    }

    #endregion

    #region Win32 interop

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const int GwlStyle = -16;     // GetWindowLongPtr index for style
    private const int GwlWndproc = -4;    // index for WndProc
    private const int WsChild = 0x40000000;
    private const int WsVisible = 0x10000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetClientRect(IntPtr hWnd, out Rect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    #endregion
}
#endif
