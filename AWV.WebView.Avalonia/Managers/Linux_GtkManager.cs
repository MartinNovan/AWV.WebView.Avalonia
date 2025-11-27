using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AWV.WebView.Avalonia.Managers;

// Runs GTK in a dedicated thread, handles queued actions and event loop.
// Basically isolates GTK/WebKit from Avalonia UI thread so it doesn't hang.
internal static class GtkManager
{
    private static Thread? _gtkThread;
    private static readonly ConcurrentQueue<GtkAction> Queue = new();
    private static readonly AutoResetEvent Signal = new(false);
    private static volatile bool _running;
    private static volatile bool _initialized;

    private record GtkAction(Func<object?> Action, TaskCompletionSource<object?> Tcs);

    // Ensures GTK thread exists before using any GTK/WebKit API.
    public static void EnsureInitialized()
    {
        if (_initialized) return;

        lock (typeof(GtkManager))
        {
            if (_initialized) return;

            _gtkThread = new Thread(GtkThreadLoop)
            {
                IsBackground = true,
                Name = "GTK-Thread"
            };
            _gtkThread.Start();

            // Wait for GTK init (timeout = 5s)
            var sw = Stopwatch.StartNew();
            while (!_initialized)
            {
                if (sw.ElapsedMilliseconds > 5000)
                    throw new TimeoutException("GTK thread failed to initialize in 5 seconds.");
                Thread.Sleep(10);
            }
        }
    }

    // Entry point for GTK thread. Initializes GTK and processes both events and queued actions.
    private static void GtkThreadLoop()
    {
        try
        {
            int argc = 0;
            IntPtr argv = IntPtr.Zero;
            var ok = gtk_init_check(ref argc, ref argv);
            if (!ok)
                throw new InvalidOperationException("gtk_init_check failed");

            _running = true;
            _initialized = true;

            while (_running)
            {
                // Pump GTK events
                while (gtk_events_pending())
                    gtk_main_iteration_do(false);

                // Process one queued action if available
                if (Queue.TryDequeue(out var item))
                {
                    try
                    {
                        var res = item.Action();
                        item.Tcs.SetResult(res);
                    }
                    catch (Exception ex)
                    {
                        item.Tcs.SetException(ex);
                    }
                    continue;
                }

                // Wait a bit to avoid CPU spin, still lets GTK pump every few ms
                Signal.WaitOne(10);
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("GTK thread exception: " + e);
            throw;
        }
    }

    // Async invoke — runs the given delegate on GTK thread and returns Task.
    private static Task<object?> InvokeOnGtkThreadAsync(Func<object?> func)
    {
        var tcs = new TaskCompletionSource<object?>();
        Queue.Enqueue(new GtkAction(func, tcs));
        Signal.Set();
        return tcs.Task;
    }

    // Sync invoke — waits until action finishes on GTK thread.
    private static object? InvokeOnGtkThread(Func<object?> func)
    {
        var task = InvokeOnGtkThreadAsync(func);
        task.Wait();
        return task.Result;
    }

    // Creates a GtkPlug (child widget) parented into an external X11 window (Avalonia's host),
    // inserts a WebKit WebView into it and returns raw pointers for later teardown.
    // Must be called after EnsureInitialized; execution is marshalled to the GTK thread.
    // Creates GtkPlug + WebKit WebView inside it, attaches to provided X11 parent window.
    // This is called from Avalonia side and executed safely inside GTK thread.
    public static GtkPlugWithWebView CreatePlugWithWebView(ulong parentXid, string? url)
    {
        EnsureInitialized();

        var result = InvokeOnGtkThread(() =>
        {
            IntPtr plug = gtk_plug_new(parentXid);
            if (plug == IntPtr.Zero)
                throw new InvalidOperationException("gtk_plug_new returned null");

            IntPtr webview = webkit_web_view_new();
            if (webview == IntPtr.Zero)
                throw new InvalidOperationException("webkit_web_view_new returned null");

            // Attach WebView to GtkPlug (GtkContainer)
            gtk_container_add(plug, webview);

            // Load initial URL
            webkit_web_view_load_uri(webview, url ?? "about:blank");

            // Make everything visible (forces GdkWindow creation)
            gtk_widget_show_all(plug);

            // Grab XID of realized GtkPlug so Avalonia can embed it
            IntPtr gdkWin = gtk_widget_get_window(plug);
            if (gdkWin == IntPtr.Zero)
                throw new InvalidOperationException("gtk_widget_get_window returned null");

            ulong plugXid = gdk_x11_window_get_xid(gdkWin);

            return new GtkPlugWithWebView
            {
                Plug = plug,
                WebView = webview,
                PlugXid = plugXid
            };
        });

        return (GtkPlugWithWebView)result!;
    }

    // Destroys GtkPlug + WebView (safe to call anytime). Exceptions are ignored on purpose.
    public static void DestroyPlug(GtkPlugWithWebView? plug)
    {
        if (plug == null) return;

        InvokeOnGtkThread(() =>
        {
            try
            {
                if (plug.WebView != IntPtr.Zero)
                    gtk_widget_destroy(plug.WebView);
            }
            catch { /* ignored */ }

            try
            {
                if (plug.Plug != IntPtr.Zero)
                    gtk_widget_destroy(plug.Plug);
            }
            catch { /* ignored */ }

            return null;
        });
    }

    #region Native bindings
    private const string LibGtk = "libgtk-3.so.0";
    private const string LibGdk = "libgdk-3.so.0";
    private const string LibWebKit = "libwebkit2gtk-4.1.so.0";
    // Note: This targets GTK3 + WebKitGTK 4.1 on X11 (no Wayland support).

    [DllImport(LibGtk, CharSet = CharSet.Ansi, EntryPoint = "gtk_init_check")]
    private static extern bool gtk_init_check(ref int argc, ref IntPtr argv);

    [DllImport(LibGtk, EntryPoint = "gtk_events_pending")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool gtk_events_pending();

    [DllImport(LibGtk, EntryPoint = "gtk_main_iteration_do")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool gtk_main_iteration_do([MarshalAs(UnmanagedType.I1)] bool blocking);

    [DllImport(LibGtk, EntryPoint = "gtk_plug_new")]
    private static extern IntPtr gtk_plug_new(ulong socketId);

    [DllImport(LibGtk, EntryPoint = "gtk_widget_show_all")]
    private static extern void gtk_widget_show_all(IntPtr widget);

    [DllImport(LibGtk, EntryPoint = "gtk_container_add")]
    private static extern void gtk_container_add(IntPtr container, IntPtr widget);

    [DllImport(LibGtk, EntryPoint = "gtk_widget_destroy")]
    private static extern void gtk_widget_destroy(IntPtr widget);

    [DllImport(LibGtk, EntryPoint = "gtk_widget_get_window")]
    private static extern IntPtr gtk_widget_get_window(IntPtr widget);

    [DllImport(LibGdk, EntryPoint = "gdk_x11_window_get_xid")]
    private static extern ulong gdk_x11_window_get_xid(IntPtr gdkWindow);

    [DllImport(LibWebKit, EntryPoint = "webkit_web_view_new")]
    private static extern IntPtr webkit_web_view_new();

    [DllImport(LibWebKit, CharSet = CharSet.Ansi, EntryPoint = "webkit_web_view_load_uri")]
    private static extern void webkit_web_view_load_uri(IntPtr webView, string uri);
    #endregion
}

// Keeps native pointers for GtkPlug + WebView
internal sealed class GtkPlugWithWebView
{
    public IntPtr Plug { get; init; }
    public IntPtr WebView { get; init; }
    public ulong PlugXid { get; init; }
}
