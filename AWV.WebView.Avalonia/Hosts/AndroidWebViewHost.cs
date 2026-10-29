#if ANDROID
using Android.Webkit;
using Android.Views;
using Avalonia.Android;
using Avalonia.Platform;
using AWV.WebView.Avalonia.Interfaces;

namespace AWV.WebView.Avalonia.Hosts
{
    /// <summary>
    /// Android implementation hosted through Avalonia's AndroidViewControlHandle.
    /// Avalonia owns the native container handle; this class owns the WebView
    /// lifecycle and forwards navigation events.
    /// </summary>
    internal sealed class AndroidWebViewHost : WebViewHost.IWebViewHost, WebViewHost.INavigationHost
    {
        private global::Android.Webkit.WebView? _webView;   // the actual WebView
        private FrameLayout? _container;                     // container to host WebView, like a panel
        private WebViewNavigationPolicy _policy = new();
        private bool _loading;
        private string _pendingUrl = "about:blank";
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
            _webView?.LoadUrl(url);
        }

        public void GoBack() { if (_webView?.CanGoBack() == true) _webView.GoBack(); }
        public void GoForward() { if (_webView?.CanGoForward() == true) _webView.GoForward(); }
        public void Reload() => _webView?.Reload();
        public void Stop()
        {
            _webView?.StopLoading();
            _loading = false;
            PublishNavigationState();
        }

        private void PublishNavigationState()
        {
            if (_destroyed) return;
            var view = _webView;
            NavigationStateChanged?.Invoke(new WebViewHost.NavigationSnapshot(
                view?.Url ?? string.Empty, _loading,
                view?.CanGoBack() ?? false, view?.CanGoForward() ?? false));
        }

        private bool MayNavigate(string url)
        {
            if (_policy.IsAllowed(url, out var why)) return true;
            NavigationBlocked?.Invoke(url, why);
            return false;
        }


        // Create WebView + container, setup basic settings and navigate to URL.
        public IPlatformHandle Create(IPlatformHandle parent, string? url)
        {
            _destroyed = false;
            _pendingUrl = string.IsNullOrWhiteSpace(url) ? "about:blank" : url;
            // Native controls must use the UI parent's context (usually the Activity)
            // so they are attached to the right window/theme/lifecycle.
            var context = (parent as AndroidViewControlHandle)?.View.Context
                ?? Application.Context;

            // create container to hold WebView
            _container = new FrameLayout(context)
            {
                LayoutParameters = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent
                )
            };

            // create WebView and fill container
            _webView = new global::Android.Webkit.WebView(context)
            {
                LayoutParameters = new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MatchParent,
                    ViewGroup.LayoutParams.MatchParent
                )
            };

            // enable common web settings
            var settings = _webView.Settings;
            settings.JavaScriptEnabled = true;
            settings.DomStorageEnabled = true;
            settings.LoadsImagesAutomatically = true;
            // Required for local file:// pages. Do not grant those pages
            // unrestricted access to other file:// resources or HTTP origins.
            settings.AllowFileAccess = true;
            settings.AllowFileAccessFromFileURLs = false;
            settings.AllowUniversalAccessFromFileURLs = false;
            settings.MixedContentMode = MixedContentHandling.NeverAllow;

            // assign safe WebViewClient to handle redirects safely
            _webView.SetWebViewClient(new SafeWebViewClient(this));

            // Attach before navigating so that Android can measure and draw it.
            _container.AddView(_webView);
            Navigate(_pendingUrl);

            // A raw PlatformHandle(pointer, "AndroidView") is NOT equivalent:
            // Avalonia Android needs a reference to the actual native View.
            return new AndroidViewControlHandle(_container);
        }

        // Destroy WebView and container safely
        public void Destroy(IPlatformHandle control)
        {
            _destroyed = true;
            try
            {
                _webView?.StopLoading();
                _webView?.Destroy();
                _webView = null;

                _container?.RemoveAllViews();
                // NativeControlHost/base.DestroyNativeControlCore disposes the
                // AndroidViewControlHandle and its container; do not dispose
                // the same native view twice here.
                _container = null;
            }
            catch
            {
                // ignore errors during cleanup
            }
        }

        // Navigation policy is applied to user clicks and HTTP redirects.
        // Android does not call ShouldOverrideUrlLoading for application-initiated
        // LoadUrl calls (those are checked in Navigate), and does not guarantee
        // callbacks for POST requests. Do not use this as a security sandbox.
        private sealed class SafeWebViewClient(AndroidWebViewHost host) : WebViewClient
        {
            public override bool ShouldOverrideUrlLoading(Android.Webkit.WebView? view, IWebResourceRequest? request)
            {
                if (request?.Url == null || !request.IsForMainFrame)
                    return false;
                var target = request.Url.ToString();
                return target != null && !host.MayNavigate(target);
            }

            public override void OnPageStarted(Android.Webkit.WebView? view, string? url, Android.Graphics.Bitmap? favicon)
            {
                base.OnPageStarted(view, url, favicon);
                if (url != null && !host.MayNavigate(url))
                {
                    view?.StopLoading();
                    host._loading = false;
                }
                else
                {
                    host._loading = true;
                }
                host.PublishNavigationState();
            }

            public override void OnPageFinished(Android.Webkit.WebView? view, string? url)
            {
                base.OnPageFinished(view, url);
                host._loading = false;
                host.PublishNavigationState();
            }

            public override void OnReceivedError(Android.Webkit.WebView? view, IWebResourceRequest? request, WebResourceError? error)
            {
                base.OnReceivedError(view, request, error);
                if (request?.IsForMainFrame == true)
                {
                    host._loading = false;
                    host.PublishNavigationState();
                }
                Android.Util.Log.Error("AWV.WebView",
                    $"Load error: {error?.Description} @ {request?.Url}");
            }
        }
    }
}
#endif
