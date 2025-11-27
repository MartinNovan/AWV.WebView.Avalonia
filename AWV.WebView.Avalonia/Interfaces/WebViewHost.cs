using Avalonia.Platform;

namespace AWV.WebView.Avalonia.Interfaces;

public abstract class WebViewHost
{
    public interface IWebViewHost
    {
        // 2 basic functions, probably i would need to add something like redirect or something like that in future, for now it just shows web window u can redirect inside but not using the code.
        IPlatformHandle Create(IPlatformHandle parent, string? url);
        void Destroy(IPlatformHandle control);
    }
}