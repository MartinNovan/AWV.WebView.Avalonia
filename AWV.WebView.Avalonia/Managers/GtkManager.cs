using System.Collections.Concurrent;
using AWV.WebView.Avalonia.Interfaces;

namespace AWV.WebView.Avalonia.Managers;

// Runs GTK in a dedicated thread, handles queued actions and event loop.
// Basically isolates GTK/WebKit from Avalonia UI thread so it doesn't hang.
//
// ARCHITECTURE NOTE (why this no longer uses GtkPlug/XEmbed):
// Wayland's security model deliberately forbids one process from embedding a
// window owned by another process using the XEmbed protocol. We instead
// render WebKitGTK into a GtkOffscreenWindow, read its Cairo pixels and copy
// them to an Avalonia bitmap. This avoids XEmbed, but remains dependent on
// the distro's GTK/WebKit implementation and needs native Wayland testing.
// Input travels the other way: Avalonia forwards pointer/keyboard events as
// synthetic GdkEvents injected via gdk_event_put.
internal static partial class GtkManager
{
    private static readonly object InitializationGate = new();
    private static readonly ManualResetEventSlim InitializationCompleted = new(false);
    private static Thread? _gtkThread;
    private static Exception? _threadFailure;
    private static readonly ConcurrentQueue<GtkAction> Queue = new();
    private static readonly AutoResetEvent Signal = new(false);
    private static volatile bool _initialized;

    // WebKitGTK's native callbacks must never propagate managed exceptions
    // through GObject. Navigation state is sampled only on the GTK thread,
    // rather than observed through several native notify:: callbacks.
    private static readonly HashSet<OffscreenWebView> LiveViews = [];
    private static long _lastNavigationPollMs;
    private static readonly bool TraceNative =
        Environment.GetEnvironmentVariable("AWV_NATIVE_TRACE") == "1";
    private static readonly bool DisableNativePolicy =
        Environment.GetEnvironmentVariable("AWV_LINUX_DISABLE_POLICY_CALLBACK") == "1";
    private static readonly bool DisableNavigationPolling =
        Environment.GetEnvironmentVariable("AWV_LINUX_DISABLE_NAV_POLL") == "1";

    private static void Trace(string message)
    {
        if (TraceNative) Console.Error.WriteLine($"[AWV native] {message}");
    }

    private record GtkAction(Action Action, TaskCompletionSource<object?>? Tcs);

    /// <summary>
    /// Starts GTK once, on its own thread, and reports initialization failure to
    /// the caller instead of throwing on a background thread (which would
    /// terminate the whole process).
    /// </summary>
    public static void EnsureInitialized()
    {
        if (_initialized) return;

        lock (InitializationGate)
        {
            if (_gtkThread == null)
            {
                // These environment variables must be set before WebKitGTK
                // initializes. Offscreen Cairo rendering does not need a GL
                // context; this avoids known DMA-BUF failures on some drivers.
                Environment.SetEnvironmentVariable("WEBKIT_DISABLE_DMABUF_RENDERER", "1");

                _gtkThread = new Thread(GtkThreadLoop)
                {
                    IsBackground = true,
                    Name = "AWV GTK"
                };
                _gtkThread.Start();
            }
        }

        if (!InitializationCompleted.Wait(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("GTK initialization did not complete within 10 seconds.");

        if (_threadFailure is not null)
            throw new InvalidOperationException("GTK initialization failed.", _threadFailure);
    }

    private static void GtkThreadLoop()
    {
        try
        {
            int argc = 0;
            IntPtr argv = IntPtr.Zero;
            Trace("GTK init: begin");
            if (!gtk_init_check(ref argc, ref argv))
                throw new InvalidOperationException("gtk_init_check failed. GTK3 and a usable display are required.");

            Trace("GTK init: complete");
            _initialized = true;
            InitializationCompleted.Set();

            while (true)
            {
                // Video/animations may keep the GTK event queue nonempty
                // indefinitely. Bound the batch so our own input/teardown
                // operations still run and GTK cannot monopolize a CPU core.
                for (var i = 0; i < 32 && gtk_events_pending(); i++)
                    gtk_main_iteration_do(false);

                for (var i = 0; i < 64 && Queue.TryDequeue(out var item); i++)
                {
                    try
                    {
                        item.Action();
                        item.Tcs?.TrySetResult(null);
                    }
                    catch (Exception ex)
                    {
                        if (item.Tcs != null)
                            item.Tcs.TrySetException(ex);
                        else
                            Console.Error.WriteLine($"[AWV GTK] queued operation failed: {ex}");
                    }
                }

                // Poll on GTK's own thread; never call native APIs from Avalonia.
                // Native notify:: and load-changed callbacks were introduced in
                // v8 and are removed here to isolate native startup failures.
                if (!DisableNavigationPolling &&
                    Environment.TickCount64 - _lastNavigationPollMs >= 125)
                {
                    _lastNavigationPollMs = Environment.TickCount64;
                    foreach (var ov in LiveViews)
                    {
                        try { PublishNavigationState(ov); }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[AWV GTK] navigation poll failed: {ex}");
                        }
                    }
                }

                if (gtk_events_pending() || !Queue.IsEmpty)
                    Thread.Sleep(1); // Yield to the desktop and GStreamer threads.
                else
                    Signal.WaitOne(10);
            }
        }
        catch (Exception ex)
        {
            _threadFailure = ex;
            _initialized = false;
            InitializationCompleted.Set();
            Console.Error.WriteLine($"[AWV GTK] thread stopped: {ex}");

            while (Queue.TryDequeue(out var item))
                item.Tcs?.TrySetException(ex);
        }
    }

    // Never make GTK calls on Avalonia's UI thread. High-frequency input can
    // be posted asynchronously; setup/teardown waits for a bounded duration.
    private static void PostToGtkThread(Action action)
    {
        if (_threadFailure is not null) return;
        Queue.Enqueue(new GtkAction(action, null));
        Signal.Set();
    }

    private static void InvokeOnGtkThread(Action action)
    {
        if (_threadFailure is not null)
            throw new InvalidOperationException("GTK is not available.", _threadFailure);

        // Prevent a thread crash or deadlock from blocking the UI indefinitely.
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Queue.Enqueue(new GtkAction(action, tcs));
        Signal.Set();
        tcs.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }

    private static T InvokeOnGtkThread<T>(Func<T> func)
    {
        T result = default!;
        // Use a statement-bodied lambda so overload resolution selects
        // InvokeOnGtkThread(Action), not this Func<T> overload again.
        InvokeOnGtkThread(() =>
        {
            result = func();
        });
        return result;
    }

    // ---------------------------------------------------------------------
    // Offscreen webview lifecycle
    // ---------------------------------------------------------------------

    public static OffscreenWebView CreateOffscreenWebView(
        int width, int height, string? url,
        WebViewNavigationPolicy policy,
        Action<WebViewHost.NavigationSnapshot> onState,
        Action<string, string> onBlocked)
    {
        EnsureInitialized();

        return InvokeOnGtkThread(() =>
        {
            var ov = new OffscreenWebView
            {
                Policy = policy,
                NavigationChanged = onState,
                NavigationRejected = onBlocked
            };
            var webViewAddedToWindow = false;

            try
            {
                Trace("creating GtkOffscreenWindow");
                ov.OffscreenWindow = gtk_offscreen_window_new();
                if (ov.OffscreenWindow == IntPtr.Zero)
                    throw new InvalidOperationException("gtk_offscreen_window_new returned null");

                Trace("creating WebKitWebView");
                ov.WebView = webkit_web_view_new();
                if (ov.WebView == IntPtr.Zero)
                    throw new InvalidOperationException("webkit_web_view_new returned null");

                // Offscreen Cairo rendering must not request GPU acceleration.
                var settings = webkit_web_view_get_settings(ov.WebView);
                if (settings != IntPtr.Zero)
                    webkit_settings_set_hardware_acceleration_policy(settings, WebKitHardwareAccelerationPolicyNever);

                gtk_container_add(ov.OffscreenWindow, ov.WebView);
                webViewAddedToWindow = true;

                var w = Math.Max(1, width);
                var h = Math.Max(1, height);
                gtk_widget_set_size_request(ov.OffscreenWindow, w, h);
                gtk_widget_set_size_request(ov.WebView, w, h);

                // Synthetic mouse/keyboard events need explicit GTK3 masks.
                gtk_widget_add_events(ov.WebView,
                    GdkPointerMotionMask |
                    GdkButtonPressMask |
                    GdkButtonReleaseMask |
                    GdkScrollMask |
                    GdkKeyPressMask |
                    GdkKeyReleaseMask);
                gtk_widget_set_can_focus(ov.WebView, true);

                // The managed callback stays rooted until the GTK widget dies.
                ov.DamageHandler = (_, _, _) =>
                {
                    // Never perform a potentially expensive Cairo snapshot
                    // inside the native damage callback. Coalesce redraws and
                    // capture later on the GTK thread (after its event batch).
                    if (ov.IsDestroyed ||
                        Interlocked.CompareExchange(ref ov.CaptureScheduled, 1, 0) != 0)
                        return false;

                    PostToGtkThread(() =>
                    {
                        try
                        {
                            if (!ov.IsDestroyed)
                                EmitFrame(ov);
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[AWV GTK] frame capture failed: {ex}");
                        }
                        finally
                        {
                            Interlocked.Exchange(ref ov.CaptureScheduled, 0);
                        }
                    });
                    return false;
                };
                g_signal_connect_data(ov.OffscreenWindow, "damage-event", ov.DamageHandler, IntPtr.Zero, IntPtr.Zero, 0);

                // The policy signal is synchronous because a navigation must
                // be accepted or rejected before WebKit continues. Keep the
                // managed delegate alive until after GTK destroys the widget.
                // An opt-out allows isolating ABI / signal issues on a user's
                // particular WebKitGTK version. Disabling it means in-page
                // navigation is NOT checked against policy during this test.
                if (!DisableNativePolicy)
                {
                    ov.PolicyHandler = (_, decision, kind, _) =>
                    {
                        try
                        {
                            if (ov.IsDestroyed) return 0;
                            if (kind == 1)
                            {
                                var target = GetDecisionUri(decision);
                                ov.NavigationRejected?.Invoke(target, "New windows are not supported");
                                webkit_policy_decision_ignore(decision);
                                return 1;
                            }
                            if (kind != 0) return 0; // response policy uses default
                            var destination = GetDecisionUri(decision);
                            if (policy.IsAllowed(destination, out var why)) return 0;
                            ov.NavigationRejected?.Invoke(destination, why);
                            webkit_policy_decision_ignore(decision);
                            return 1;
                        }
                        catch (Exception ex)
                        {
                            // Do not allow managed exceptions to cross native
                            // GObject signal callback boundaries.
                            Console.Error.WriteLine($"[AWV GTK] decide-policy failed: {ex}");
                            return 0; // let WebKit's default handler decide
                        }
                    };
                    Trace("connecting decide-policy");
                    g_signal_connect_data(ov.WebView, "decide-policy", ov.PolicyHandler, IntPtr.Zero, IntPtr.Zero, 0);
                }
                else
                {
                    Console.Error.WriteLine("[AWV GTK] DIAGNOSTIC ONLY: decide-policy disabled; in-page navigation is not filtered.");
                }

                Trace("show GTK offscreen window");
                gtk_widget_show_all(ov.OffscreenWindow);
                var initial = string.IsNullOrEmpty(url) ? "about:blank" : url;
                LiveViews.Add(ov);
                try
                {
                    if (policy.IsAllowed(initial, out var reason))
                    {
                        Trace("navigate initial URI");
                        webkit_web_view_load_uri(ov.WebView, initial);
                    }
                    else
                        ov.NavigationRejected?.Invoke(initial, reason);
                    // First snapshot happens on the next poll, outside
                    // WebKit's synchronous navigation/initialization path.
                    Trace("WebView created");
                    return ov;
                }
                catch
                {
                    LiveViews.Remove(ov);
                    throw;
                }
            }
            catch
            {
                // If child installation failed, GTK does not own the child yet.
                if (ov.OffscreenWindow != IntPtr.Zero)
                    gtk_widget_destroy(ov.OffscreenWindow);
                if (!webViewAddedToWindow && ov.WebView != IntPtr.Zero)
                    gtk_widget_destroy(ov.WebView);
                throw;
            }
        });
    }

    public static void ResizeOffscreenWebView(OffscreenWebView ov, int width, int height)
    {
        if (ov.IsDestroyed) return;
        PostToGtkThread(() =>
        {
            if (ov.IsDestroyed) return;
            var w = Math.Max(1, width);
            var h = Math.Max(1, height);
            gtk_widget_set_size_request(ov.OffscreenWindow, w, h);
            gtk_widget_set_size_request(ov.WebView, w, h);
            gtk_widget_queue_resize(ov.OffscreenWindow);
        });
    }

    public static void NavigateOffscreenWebView(OffscreenWebView ov, string url)
    {
        if (ov.IsDestroyed) return;
        PostToGtkThread(() =>
        {
            if (ov.IsDestroyed) return;
            if (ov.Policy is { } activePolicy && !activePolicy.IsAllowed(url, out var why))
            {
                ov.NavigationRejected?.Invoke(url, why);
                return;
            }
            webkit_web_view_load_uri(ov.WebView, url);
        });
    }

    public static void GoBack(OffscreenWebView ov) => PostIfAlive(ov, () =>
    {
        if (webkit_web_view_can_go_back(ov.WebView) != 0) webkit_web_view_go_back(ov.WebView);
    });

    public static void GoForward(OffscreenWebView ov) => PostIfAlive(ov, () =>
    {
        if (webkit_web_view_can_go_forward(ov.WebView) != 0) webkit_web_view_go_forward(ov.WebView);
    });

    public static void Reload(OffscreenWebView ov) => PostIfAlive(ov, () => webkit_web_view_reload(ov.WebView));
    public static void Stop(OffscreenWebView ov) => PostIfAlive(ov, () => webkit_web_view_stop_loading(ov.WebView));

    private static void PostIfAlive(OffscreenWebView ov, Action command)
    {
        if (!ov.IsDestroyed)
            PostToGtkThread(() => { if (!ov.IsDestroyed) command(); });
    }

    private static string GetDecisionUri(IntPtr decision)
    {
        var action = webkit_navigation_policy_decision_get_navigation_action(decision);
        if (action == IntPtr.Zero) return string.Empty;
        var request = webkit_navigation_action_get_request(action);
        if (request == IntPtr.Zero) return string.Empty;
        return System.Runtime.InteropServices.Marshal.PtrToStringUTF8(webkit_uri_request_get_uri(request)) ?? string.Empty;
    }

    private static void PublishNavigationState(OffscreenWebView ov)
    {
        if (ov.IsDestroyed || ov.WebView == IntPtr.Zero) return;
        var snapshot = new WebViewHost.NavigationSnapshot(
            System.Runtime.InteropServices.Marshal.PtrToStringUTF8(webkit_web_view_get_uri(ov.WebView)) ?? string.Empty,
            webkit_web_view_is_loading(ov.WebView) != 0,
            webkit_web_view_can_go_back(ov.WebView) != 0,
            webkit_web_view_can_go_forward(ov.WebView) != 0);

        if (ov.LastNavigationSnapshot == snapshot) return;
        ov.LastNavigationSnapshot = snapshot;
        ov.NavigationChanged?.Invoke(snapshot);
    }

    public static void DestroyOffscreenWebView(OffscreenWebView? ov)
    {
        if (ov == null || !ov.MarkDestroyed()) return;

        // Mark destroyed before queuing cleanup: old mouse/keyboard operations
        // must not access widgets that are about to be freed.
        InvokeOnGtkThread(() =>
        {
            try
            {
                LiveViews.Remove(ov);
                // Destroying the parent also destroys its child WebKitWebView.
                if (ov.OffscreenWindow != IntPtr.Zero)
                    gtk_widget_destroy(ov.OffscreenWindow);
            }
            finally
            {
                ov.WebView = IntPtr.Zero;
                ov.OffscreenWindow = IntPtr.Zero;
                ov.DamageHandler = null;
                ov.PolicyHandler = null;
                ov.NavigationChanged = null;
                ov.NavigationRejected = null;
            }
        });
    }

    // Rendering is CPU-bound (Cairo snapshot -> managed array -> Avalonia).
    // 30fps is the default; lower it for troubleshooting heavy/video pages:
    // AWV_LINUX_MAX_FPS=10 dotnet run
    private static readonly long MinFrameIntervalMs = GetFrameIntervalMs();

    private static long GetFrameIntervalMs()
    {
        var setting = Environment.GetEnvironmentVariable("AWV_LINUX_MAX_FPS");
        return int.TryParse(setting, out var fps) && fps is >= 1 and <= 60
            ? Math.Max(16, 1000 / fps)
            : 33; // ~30fps default
    }

    private static void EmitFrame(OffscreenWebView ov)
    {
        if (ov.IsDestroyed) return;
        var now = Environment.TickCount64;
        if (now - ov.LastFrameTimestampMs < MinFrameIntervalMs)
            return; // drop this tick - a following damage-event during the
                     // same animation burst will catch up and carry the
                     // up-to-date pixels, so we're not accumulating staleness
        ov.LastFrameTimestampMs = now;

        var surface = gtk_offscreen_window_get_surface(ov.OffscreenWindow);
        if (surface == IntPtr.Zero) return;

        var imageSurface = surface;
        var ownsImageSurface = false;

        try
        {
            if (cairo_surface_get_type(surface) != CairoSurfaceTypeImage)
            {
                // X11 and some other GDK backends return a non-image cairo
                // surface. Copy it to ARGB32 so pixel access works everywhere.
                var w = gtk_widget_get_allocated_width(ov.OffscreenWindow);
                var h = gtk_widget_get_allocated_height(ov.OffscreenWindow);
                if (w <= 0 || h <= 0) return;

                imageSurface = cairo_image_surface_create(CairoFormatArgb32, w, h);
                if (imageSurface == IntPtr.Zero) return;
                ownsImageSurface = true;

                var context = cairo_create(imageSurface);
                try
                {
                    cairo_set_source_surface(context, surface, 0, 0);
                    cairo_paint(context);
                }
                finally
                {
                    cairo_destroy(context);
                }
            }

            cairo_surface_flush(imageSurface);
            var data = cairo_image_surface_get_data(imageSurface);
            var width = cairo_image_surface_get_width(imageSurface);
            var height = cairo_image_surface_get_height(imageSurface);
            var stride = cairo_image_surface_get_stride(imageSurface);

            if (data != IntPtr.Zero && width > 0 && height > 0 && stride >= (long)width * 4)
                ov.RaiseFrameReady(new WebViewHost.WebViewFrame(data, width, height, stride));
        }
        finally
        {
            // A FrameReady callback receives only a borrowed cairo pointer.
            // Consumers must copy its bytes before this block returns.
            if (ownsImageSurface)
                cairo_surface_destroy(imageSurface);
        }
    }

}
