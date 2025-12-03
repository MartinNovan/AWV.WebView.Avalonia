using System.Globalization;
using Avalonia.Platform;
using AWV.WebView.Avalonia.Interfaces;
using AWV.WebView.Avalonia.Managers;

namespace AWV.WebView.Avalonia.Hosts;

// Linux/X11 implementation of WebView host using GTK + WebKit.
// Creates a GtkPlug inside Avalonia window and embeds WebKit WebView.
public class LinuxWebViewHost : WebViewHost.IWebViewHost
{
    private GtkPlugWithWebView? _nativePlug; // stores native GtkPlug + WebView

    // Creates GtkPlug + WebView and wraps XID in Avalonia's PlatformHandle.
    public IPlatformHandle Create(IPlatformHandle parent, string? url)
    {
        if (parent == null)
            throw new InvalidOperationException("Native parent handle is required for WebView on Linux/X11.");

        // Get raw platform handle (could be boxed as string, IntPtr, nint, etc.)
        object handleObj = parent.Handle;

        // Convert handle to ulong XID (supports decimal, hex string, IntPtr, numeric)
        ulong parentXid;
        try
        {
            if (handleObj is string sHandle)
            {
                if (sHandle.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ulong.TryParse(sHandle.Substring(2), NumberStyles.HexNumber, null, out parentXid))
                        throw new FormatException("Invalid hex XID: " + sHandle);
                }
                else if (!ulong.TryParse(sHandle, out parentXid))
                {
                    // try hex without 0x prefix
                    if (!ulong.TryParse(sHandle, NumberStyles.HexNumber, null, out parentXid))
                        throw new FormatException("Invalid XID string: " + sHandle);
                }
            }
            else if (handleObj is IntPtr iptr)
            {
                parentXid = unchecked((ulong)iptr.ToInt64());
            }
            else if (handleObj is nint ni)
            {
                parentXid = unchecked((ulong)ni);
            }
            else
            {
                // fallback to Convert
                parentXid = Convert.ToUInt64(handleObj);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to parse parent XID: " + parent.Handle, ex);
        }

        // make sure GTK thread is running before creating plug
        GtkManager.EnsureInitialized();

        // create GtkPlug + WebView synchronously on GTK thread
        var _url = string.IsNullOrEmpty(url) ? "about:blank" : url; // Load blank page when passed url is wrong else load the passed one
        _nativePlug = GtkManager.CreatePlugWithWebView(parentXid, _url);

        // return plug XID as Avalonia native control handle
        return new PlatformHandle((nint)(long)_nativePlug.PlugXid, "XID");
    }

    // Destroys native resources when Avalonia control is destroyed.
    public void Destroy(IPlatformHandle control)
    {
        if (_nativePlug != null)
        {
            try
            {
                GtkManager.DestroyPlug(_nativePlug); // safely destroy GtkPlug + WebView
            }
            catch
            {
                // ignored, don't crash app on shutdown
            }

            _nativePlug = null; // release reference
        }
    }
    
    // When i get mentally sane mby i will try to implement propre wayland support natively,
    // but my attemps were destroyed by the damn design of the wayland 
    // need to know more how to connect webview window to app in wayland
}
