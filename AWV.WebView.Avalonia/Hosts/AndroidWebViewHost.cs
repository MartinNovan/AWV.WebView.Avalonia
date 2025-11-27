#if ANDROID
using Android.Webkit;
using Android.Widget;
using Android.Views;
using Avalonia.Platform;
using AWV.WebView.Avalonia.Interfaces;

namespace AWV.WebView.Avalonia.Hosts
{
    // Android implementation of WebView using native Android.Webkit.WebView inside a FrameLayout container.
    public class AndroidWebViewHost : WebViewHost.IWebViewHost
    {
        private global::Android.Webkit.WebView? _webView;   // the actual WebView
        private FrameLayout? _container;                     // container to host WebView, like a panel

        // Create WebView + container, setup basic settings and navigate to URL.
        public IPlatformHandle Create(IPlatformHandle parent, string? url)
        {
            var context = Android.App.Application.Context;

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
            settings.AllowFileAccess = true;
            settings.MixedContentMode = MixedContentHandling.AlwaysAllow;

            // assign safe WebViewClient to handle redirects safely
            _webView.SetWebViewClient(new SafeWebViewClient());

            // initial navigation
            if (!string.IsNullOrEmpty(url))
                _webView.LoadUrl(url);

            // add WebView to container
            _container.AddView(_webView);

            // return container handle as Avalonia platform handle
            return new PlatformHandle(_container.Handle, "AndroidView");
        }

        // Destroy WebView and container safely
        public void Destroy(IPlatformHandle control)
        {
            try
            {
                _webView?.StopLoading();
                _webView?.Destroy();
                _webView = null;

                _container?.RemoveAllViews();
                _container?.Dispose();
                _container = null;
            }
            catch
            {
                // ignore errors during cleanup
            }
        }

        // internal WebViewClient to handle redirects and errors safely
        private class SafeWebViewClient : WebViewClient
        {
            // handle URL redirects in a controlled way
            public override bool ShouldOverrideUrlLoading(Android.Webkit.WebView? view, IWebResourceRequest? request)
            {
                if (view == null || request?.Url == null)
                    return false;

                var url = request.Url.ToString();

                // allow normal HTTP/HTTPS redirects
                if (url != null && (url.StartsWith("http://") || url.StartsWith("https://")))
                {
                    view.LoadUrl(url);
                    return true;
                }

                // block or handle other schemes (intent:, mailto:, etc.), maybe add option later
                return true;
            }

            // log WebView errors for debugging
            public override void OnReceivedError(Android.Webkit.WebView? view, IWebResourceRequest? request, WebResourceError? error)
            {
                base.OnReceivedError(view, request, error);

                var failingUrl = request?.Url?.ToString() ?? "unknown";
                var description = error?.Description ?? "no description";
                var code = (int?)error?.ErrorCode ?? -1;

                System.Console.WriteLine($"WebView error ({code}): {description} @ {failingUrl}");
            }
        }
    }
}
#endif
