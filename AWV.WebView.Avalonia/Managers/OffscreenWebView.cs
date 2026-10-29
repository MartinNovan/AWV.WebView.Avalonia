using AWV.WebView.Avalonia.Interfaces;

namespace AWV.WebView.Avalonia.Managers;

/// <summary>
/// Per-instance native GTK handles and synthesized input state. Owned by
/// <see cref="GtkManager"/>; native members are accessed on the GTK thread.
/// </summary>
internal sealed class OffscreenWebView
{
    internal IntPtr OffscreenWindow;
    internal IntPtr WebView;

    internal WebViewNavigationPolicy? Policy;
    internal Action<WebViewHost.NavigationSnapshot>? NavigationChanged;
    internal Action<string, string>? NavigationRejected;

    [System.Runtime.InteropServices.UnmanagedFunctionPointer(
        System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate int DecidePolicyDelegate(IntPtr webView, IntPtr decision, int decisionType, IntPtr data);

    internal DecidePolicyDelegate? PolicyHandler;
    internal WebViewHost.NavigationSnapshot? LastNavigationSnapshot;

    internal uint ModifierState;
    internal uint PointerState;
    internal long LastFrameTimestampMs;
    internal int CaptureScheduled;
    internal readonly object MotionGate = new();
    internal double LastMotionX;
    internal double LastMotionY;
    internal bool MotionQueued;

    private int _destroyed;
    internal bool IsDestroyed => Volatile.Read(ref _destroyed) != 0;
    internal bool MarkDestroyed() => Interlocked.Exchange(ref _destroyed, 1) == 0;

    [System.Runtime.InteropServices.UnmanagedFunctionPointer(
        System.Runtime.InteropServices.CallingConvention.Cdecl)]
    internal delegate bool DamageEventDelegate(IntPtr widget, IntPtr eventPtr, IntPtr userData);

    internal DamageEventDelegate? DamageHandler;
    internal event Action<WebViewHost.WebViewFrame>? FrameReady;

    internal void RaiseFrameReady(WebViewHost.WebViewFrame frame)
    {
        if (!IsDestroyed)
            FrameReady?.Invoke(frame);
    }
}
