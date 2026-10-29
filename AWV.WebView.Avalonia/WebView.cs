using System.Buffers;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using AWV.WebView.Avalonia.Hosts;
using AWV.WebView.Avalonia.Interfaces;
using AWV.WebView.Avalonia.Managers;
using Control = Avalonia.Controls.Control;
using KeyEventArgs = Avalonia.Input.KeyEventArgs;

namespace AWV.WebView.Avalonia;

/// <summary>
/// Embeds a platform-native browser in an Avalonia application.
/// <para>Windows uses WebView2, Android uses Android WebView, and Linux
/// uses WebKitGTK rendered into an offscreen Avalonia bitmap.</para>
/// <para>Linux's offscreen mode is experimental, particularly on Wayland;
/// see the package documentation for limitations.</para>
/// </summary>
public sealed class WebView : ContentControl, IWebViewNavigation
{
    /// <summary>
    /// Address to display. Accepts absolute URLs, file:// URIs, absolute file
    /// paths and paths relative to <see cref="AppContext.BaseDirectory"/>.
    /// </summary>
    public static readonly StyledProperty<string> UrlProperty =
        AvaloniaProperty.Register<WebView, string>(nameof(Url), string.Empty);

    /// <summary>Gets or sets the current address.</summary>
    public string Url
    {
        get => GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    /// <summary>Actual page address, including redirects and history traversal.</summary>
    public static readonly DirectProperty<WebView, string> CurrentUrlProperty =
        AvaloniaProperty.RegisterDirect<WebView, string>(nameof(CurrentUrl), w => w.CurrentUrl);
    private string _currentUrl = string.Empty;
    /// <inheritdoc/>
    public string CurrentUrl { get => _currentUrl; private set => SetAndRaise(CurrentUrlProperty, ref _currentUrl, value); }

    /// <summary>Whether back navigation is available.</summary>
    public static readonly DirectProperty<WebView, bool> CanGoBackProperty =
        AvaloniaProperty.RegisterDirect<WebView, bool>(nameof(CanGoBack), w => w.CanGoBack);
    private bool _canGoBack;
    /// <inheritdoc/>
    public bool CanGoBack { get => _canGoBack; private set => SetAndRaise(CanGoBackProperty, ref _canGoBack, value); }

    /// <summary>Whether forward navigation is available.</summary>
    public static readonly DirectProperty<WebView, bool> CanGoForwardProperty =
        AvaloniaProperty.RegisterDirect<WebView, bool>(nameof(CanGoForward), w => w.CanGoForward);
    private bool _canGoForward;
    /// <inheritdoc/>
    public bool CanGoForward { get => _canGoForward; private set => SetAndRaise(CanGoForwardProperty, ref _canGoForward, value); }

    /// <summary>Whether the browser is currently loading a page.</summary>
    public static readonly DirectProperty<WebView, bool> IsLoadingProperty =
        AvaloniaProperty.RegisterDirect<WebView, bool>(nameof(IsLoading), w => w.IsLoading);
    private bool _isLoading;
    /// <inheritdoc/>
    public bool IsLoading { get => _isLoading; private set => SetAndRaise(IsLoadingProperty, ref _isLoading, value); }

    /// <inheritdoc/>
    public WebViewNavigationPolicy NavigationPolicy { get; } = new();

    /// <inheritdoc/>
    public event EventHandler? NavigationStateChanged;
    /// <inheritdoc/>
    public event EventHandler<WebViewNavigationBlockedEventArgs>? NavigationBlocked;

    private WebViewHost.INavigationHost? _navigationHost;

    /// <inheritdoc/>
    public void Navigate(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (Url == url)
        {
            if (_navigationHost != null)
                NavigateActiveHost(url);
        }
        else
        {
            Url = url;
        }
    }

    /// <inheritdoc/>
    public void GoBack() { if (CanGoBack) _navigationHost?.GoBack(); }
    /// <inheritdoc/>
    public void GoForward() { if (CanGoForward) _navigationHost?.GoForward(); }
    /// <inheritdoc/>
    public void Reload() => _navigationHost?.Reload();
    /// <inheritdoc/>
    public void Stop() => _navigationHost?.Stop();

    private void NavigateActiveHost(string url)
    {
        var normalized = WebViewUrlNormalizer.Normalize(url);
        if (NavigationPolicy.IsAllowed(normalized, out var reason))
            _navigationHost?.Navigate(normalized);
        else
            ReportNavigationBlocked(normalized, reason);
    }

    private void OnNativeNavigationStateChanged(WebViewHost.NavigationSnapshot state)
    {
        var generation = Volatile.Read(ref _backendGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != Volatile.Read(ref _backendGeneration)) return;
            var changed = CurrentUrl != state.CurrentUrl || CanGoBack != state.CanGoBack ||
                          CanGoForward != state.CanGoForward || IsLoading != state.IsLoading;
            CurrentUrl = state.CurrentUrl;
            CanGoBack = state.CanGoBack;
            CanGoForward = state.CanGoForward;
            IsLoading = state.IsLoading;
            if (changed) NavigationStateChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnNativeNavigationBlocked(string url, string reason)
    {
        var generation = Volatile.Read(ref _backendGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            if (generation == Volatile.Read(ref _backendGeneration))
                NavigationBlocked?.Invoke(this, new WebViewNavigationBlockedEventArgs(url, reason));
        });
    }

    private void ReportNavigationBlocked(string url, string reason) =>
        NavigationBlocked?.Invoke(this, new WebViewNavigationBlockedEventArgs(url, reason));

    private Control? _contentControl;
    private LinuxWebViewHost? _linuxHost;
    private NativeHost? _nativeHost;

    // GTK renders on a worker thread. Keep no more than one transferred frame
    // in Avalonia's dispatcher queue, and reject stale frames after detach.
    private int _pendingFrame;
    private int _backendGeneration;

    /// <summary>Creates an initially empty browser control.</summary>
    public WebView()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RecreateBackend();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DestroyBackend();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == UrlProperty && IsLoaded)
        {
            if (_navigationHost != null)
                NavigateActiveHost(Url);
            else
                RecreateBackend();
        }

        if (change.Property == BoundsProperty &&
            _linuxHost != null)
        {
            var size = Bounds.Size;
            _linuxHost.Resize(
                Math.Max(1, (int)Math.Ceiling(size.Width)),
                Math.Max(1, (int)Math.Ceiling(size.Height)));
        }
    }

    private void RecreateBackend()
    {
        DestroyBackend();

        var initialUrl = WebViewUrlNormalizer.Normalize(Url);
        if (!NavigationPolicy.IsAllowed(initialUrl, out var reason))
        {
            ReportNavigationBlocked(initialUrl, reason);
            initialUrl = "about:blank";
        }

        if (OperatingSystem.IsLinux())
        {
            _linuxHost = new LinuxWebViewHost();
            _linuxHost.FrameReady += OnLinuxFrameReady;
            AttachNavigationHost(_linuxHost);

            _contentControl = new LinuxWebViewSurface(_linuxHost);

            Content = _contentControl;

            var size = Bounds.Size;
            try
            {
                _linuxHost.Create(
                    Math.Max(1, (int)Math.Ceiling(size.Width)),
                    Math.Max(1, (int)Math.Ceiling(size.Height)),
                    initialUrl);
            }
            catch
            {
                DestroyBackend();
                throw;
            }

            return;
        }

#if WINDOWS
        if (OperatingSystem.IsWindows())
        {
            var host = new WindowsWebViewHost();
            AttachNavigationHost(host);
            _nativeHost = new NativeHost(host, initialUrl);
            _contentControl = _nativeHost;
            Content = _nativeHost;
            return;
        }
#endif

#if ANDROID
        if (OperatingSystem.IsAndroid())
        {
            var host = new AndroidWebViewHost();
            AttachNavigationHost(host);
            _nativeHost = new NativeHost(host, initialUrl);
            _contentControl = _nativeHost;
            Content = _nativeHost;
            return;
        }
#endif

        throw new PlatformNotSupportedException(
            "WebView is not supported on this platform.");
    }

    private void AttachNavigationHost(WebViewHost.INavigationHost host)
    {
        _navigationHost = host;
        host.SetNavigationPolicy(NavigationPolicy);
        host.NavigationStateChanged += OnNativeNavigationStateChanged;
        host.NavigationBlocked += OnNativeNavigationBlocked;
    }

    private void DestroyBackend()
    {
        Interlocked.Increment(ref _backendGeneration);
        if (_navigationHost != null)
        {
            _navigationHost.NavigationStateChanged -= OnNativeNavigationStateChanged;
            _navigationHost.NavigationBlocked -= OnNativeNavigationBlocked;
            _navigationHost = null;
        }
        CurrentUrl = string.Empty;
        CanGoBack = false;
        CanGoForward = false;
        IsLoading = false;

        if (_linuxHost != null)
        {
            _linuxHost.FrameReady -= OnLinuxFrameReady;
            _linuxHost.Destroy();
            _linuxHost = null;
        }

        if (_contentControl is LinuxWebViewSurface surface)
            surface.DisposeFrame();

        Content = null;
        _nativeHost = null;
        _contentControl = null;
    }

    private void OnLinuxFrameReady(WebViewHost.WebViewFrame frame)
    {
        if (frame.Width <= 0 || frame.Height <= 0 ||
            frame.Stride < (long)frame.Width * 4 ||
            (long)frame.Stride * frame.Height > int.MaxValue)
            return;

        if (Interlocked.CompareExchange(ref _pendingFrame, 1, 0) != 0)
            return;

        var generation = Volatile.Read(ref _backendGeneration);
        var size = frame.Stride * frame.Height;
        byte[] rented;
        try
        {
            rented = ArrayPool<byte>.Shared.Rent(size);
        }
        catch
        {
            Interlocked.Exchange(ref _pendingFrame, 0);
            throw;
        }

        try
        {
            Marshal.Copy(frame.Data, rented, 0, size);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(rented);
            Interlocked.Exchange(ref _pendingFrame, 0);
            return;
        }

        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    if (generation == Volatile.Read(ref _backendGeneration) &&
                        _contentControl is LinuxWebViewSurface surface)
                        surface.SetFrame(rented, frame.Width, frame.Height, frame.Stride);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(rented);
                    Interlocked.Exchange(ref _pendingFrame, 0);
                }
            });
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(rented);
            Interlocked.Exchange(ref _pendingFrame, 0);
            throw;
        }
    }

    private sealed class NativeHost : NativeControlHost
    {
        private readonly WebViewHost.IWebViewHost _host;
        private readonly string _url;

        public NativeHost(WebViewHost.IWebViewHost host, string url)
        {
            _host = host;
            _url = url;
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            return _host.Create(parent, _url);
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            try
            {
                _host.Destroy(control);
            }
            finally
            {
                base.DestroyNativeControlCore(control);
            }
        }
    }

    private sealed class LinuxWebViewSurface : Control
    {
        private readonly LinuxWebViewHost _host;
        private WriteableBitmap? _bitmap;
        private int _width;
        private int _height;
        private readonly HashSet<Key> _forwardedKeys = new();
        private bool _rightAltDown;

        private static Key GetEffectiveKey(KeyEventArgs e) =>
            e.PhysicalKey == PhysicalKey.AltRight ? Key.RightAlt :
            e.PhysicalKey == PhysicalKey.AltLeft ? Key.LeftAlt : e.Key;

        private static readonly bool InputDebug =
            Environment.GetEnvironmentVariable("AWV_INPUT_DEBUG") == "1";

        public LinuxWebViewSurface(LinuxWebViewHost host)
        {
            _host = host;
            Focusable = true;
            ClipToBounds = true;
        }
        
        protected override void OnGotFocus(GotFocusEventArgs e)
        {
            base.OnGotFocus(e);
            _host.Focus(true);
        }

        protected override void OnLostFocus(RoutedEventArgs e)
        {
            foreach (var key in _forwardedKeys)
            {
                var keyval = GdkKeyMap.ToGdkKeyval(key);
                if (keyval != 0)
                    _host.KeyEvent(keyval, false);
            }
            _forwardedKeys.Clear();
            _rightAltDown = false;
            _host.Focus(false);
            base.OnLostFocus(e);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var p = e.GetPosition(this);
            _host.PointerMoved(p.X, p.Y);
            e.Handled = true;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);

            Focus();
            e.Pointer.Capture(this);

            var p = e.GetPosition(this);
            var properties = e.GetCurrentPoint(this).Properties;

            switch (properties.PointerUpdateKind)
            {
                case PointerUpdateKind.LeftButtonPressed:
                    _host.PointerPressed(p.X, p.Y, WebViewHost.WebViewPointerButton.Left);
                    break;
                case PointerUpdateKind.MiddleButtonPressed:
                    _host.PointerPressed(p.X, p.Y, WebViewHost.WebViewPointerButton.Middle);
                    break;
                case PointerUpdateKind.RightButtonPressed:
                    _host.PointerPressed(p.X, p.Y, WebViewHost.WebViewPointerButton.Right);
                    break;
            }

            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);

            var p = e.GetPosition(this);
            var properties = e.GetCurrentPoint(this).Properties;

            switch (properties.PointerUpdateKind)
            {
                case PointerUpdateKind.LeftButtonReleased:
                    _host.PointerReleased(p.X, p.Y, WebViewHost.WebViewPointerButton.Left);
                    break;
                case PointerUpdateKind.MiddleButtonReleased:
                    _host.PointerReleased(p.X, p.Y, WebViewHost.WebViewPointerButton.Middle);
                    break;
                case PointerUpdateKind.RightButtonReleased:
                    _host.PointerReleased(p.X, p.Y, WebViewHost.WebViewPointerButton.Right);
                    break;
            }

            // Captured pointers must be released; otherwise other Avalonia
            // controls can stop receiving clicks after interacting with WebView.
            if (properties is { IsLeftButtonPressed: false, IsMiddleButtonPressed: false, IsRightButtonPressed: false } &&
                Equals(e.Pointer.Captured, this))
            {
                e.Pointer.Capture(null);
            }

            e.Handled = true;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);

            var p = e.GetPosition(this);
            var delta = e.Delta;

            _host.Scrolled(p.X, p.Y, -delta.X, delta.Y);
            e.Handled = true;
        }

        protected override void OnTextInput(TextInputEventArgs e)
        {
            base.OnTextInput(e);

            if (!string.IsNullOrEmpty(e.Text))
            {
                if (InputDebug)
                    Console.Error.WriteLine($"[AWV input] commit: {e.Text}");
                _host.TextInput(e.Text);
                e.Handled = true;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            var key = GetEffectiveKey(e);
            if (InputDebug)
                Console.Error.WriteLine($"[AWV input] down: Key={e.Key} Physical={e.PhysicalKey} Symbol={e.KeySymbol} Modifiers={e.KeyModifiers} Effective={key}");

            if (key == Key.RightAlt)
                _rightAltDown = true;

            bool isPrintable = key is >= Key.A and <= Key.Z ||
                               key is >= Key.D0 and <= Key.D9;
            bool isShortcut = e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                              e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            if (isPrintable && (!isShortcut || _rightAltDown))
                return;

            var keyval = GdkKeyMap.ToGdkKeyval(key);
            if (keyval != 0)
            {
                if (_forwardedKeys.Add(key))
                    _host.KeyEvent(keyval, true);
                e.Handled = true;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);

            var key = GetEffectiveKey(e);
            if (InputDebug)
                Console.Error.WriteLine($"[AWV input] up: Key={e.Key} Physical={e.PhysicalKey} Symbol={e.KeySymbol} Modifiers={e.KeyModifiers} Effective={key}");

            if (key == Key.RightAlt)
                _rightAltDown = false;

            if (!_forwardedKeys.Remove(key))
                return;

            var keyval = GdkKeyMap.ToGdkKeyval(key);
            if (keyval != 0)
            {
                _host.KeyEvent(keyval, false);
                e.Handled = true;
            }
        }

        /// <summary>Releases the bitmap when the WebView is detached.</summary>
        public void DisposeFrame()
        {
            _bitmap?.Dispose();
            _bitmap = null;
            _width = 0;
            _height = 0;
        }

        public void SetFrame(byte[] data, int width, int height, int stride)
        {
            if (width <= 0 || height <= 0 || stride < width * 4)
                return;

            if (_bitmap == null ||
                _width != width ||
                _height != height)
            {
                _width = width;
                _height = height;
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(
                    new PixelSize(width, height),
                    new Vector(96, 96),
                    PixelFormat.Bgra8888,
                    AlphaFormat.Premul);
            }

            using var fb = _bitmap.Lock();

            if (stride == fb.RowBytes)
            {
                Marshal.Copy(data, 0, fb.Address, stride * height);
            }
            else
            {
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(
                        data,
                        y * stride,
                        IntPtr.Add(fb.Address, y * fb.RowBytes),
                        width * 4);
                }
            }

            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            if (_bitmap == null)
                return;

            context.DrawImage(
                _bitmap,
                new Rect(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height),
                new Rect(Bounds.Size));
        }
    }

}