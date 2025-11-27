using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using AWV.WebView.Avalonia.Hosts;
using AWV.WebView.Avalonia.Interfaces;

namespace AWV.WebView.Avalonia
{
    /// <summary>
    /// Cross-platform WebView control for Avalonia.
    /// Automatically chooses the appropriate platform implementation.
    /// </summary>
    public class WebView : NativeControlHost
    {
        public static readonly StyledProperty<string> UrlProperty =
            AvaloniaProperty.Register<WebView, string>(nameof(Url));

        /// <summary>
        /// Gets or sets the URL to load in the WebView.
        /// </summary>
        public string Url
        {
            get => GetValue(UrlProperty);
            set => SetValue(UrlProperty, value);
        }
        // I will probably add some other propeties or methods like (allowed url, blocked url, redirect to, safe redirects, etc.) but for now i will leave it like this
        
        private WebViewHost.IWebViewHost? _platformHost;
        
        // Creates the native control for the current platform.
        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            _platformHost = CreatePlatformHost();
            return _platformHost.Create(parent, Url);
        }
        
        // Destroys the native control and releases resources.
        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            _platformHost?.Destroy(control);
            base.DestroyNativeControlCore(control);
        }
        
        // Select the appropriate platform-specific WebView implementation.
        private static WebViewHost.IWebViewHost CreatePlatformHost()
        {
            // doesnt need to be isolated, can be compiled on other platforms with no issues
            if (OperatingSystem.IsLinux())
                return new LinuxWebViewHost();
            // Needs to be isolated, when building on linux
            #if WINDOWS
            if (OperatingSystem.IsWindows())
                return new WindowsWebViewHost();
            #endif
            // Also probably needs to be isolated, not so sure about that, but it works so i dont touch it.
            #if ANDROID
            if (OperatingSystem.IsAndroid())
                return new AndroidWebViewHost();
            #endif
            throw new PlatformNotSupportedException("WebView is not supported on this platform. If you are building for windows, check your <targetframework> if it is set to 'net9.0-windows' in your .csproj file.");
        }
    }
}
