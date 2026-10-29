using System.Runtime.InteropServices;

namespace AWV.WebView.Avalonia.Managers;

/// <summary>
/// GTK3, GDK, GLib, Cairo and WebKitGTK 4.1 P/Invoke declarations.
/// </summary>
internal static partial class GtkManager
{
    #region Native bindings

    private const string LibGtk = "libgtk-3.so.0";
    private const string LibGdk = "libgdk-3.so.0";
    private const string LibGObject = "libgobject-2.0.so.0";
    private const string LibGlib = "libglib-2.0.so.0";
    private const string LibCairo = "libcairo.so.2";
    private const string LibWebKit = "libwebkit2gtk-4.1.so.0";
 
    [DllImport(LibGtk, CharSet = CharSet.Ansi, EntryPoint = "gtk_init_check")]
    private static extern bool gtk_init_check(ref int argc, ref IntPtr argv);

    [DllImport(LibGtk, EntryPoint = "gtk_events_pending")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool gtk_events_pending();

    [DllImport(LibGtk, EntryPoint = "gtk_main_iteration_do")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool gtk_main_iteration_do([MarshalAs(UnmanagedType.I1)] bool blocking);

    [DllImport(LibGtk, EntryPoint = "gtk_offscreen_window_new")]
    private static extern IntPtr gtk_offscreen_window_new();

    [DllImport(LibGtk, EntryPoint = "gtk_offscreen_window_get_surface")]
    private static extern IntPtr gtk_offscreen_window_get_surface(IntPtr offscreenWindow);

    [DllImport(LibGtk, EntryPoint = "gtk_widget_show_all")]
    private static extern void gtk_widget_show_all(IntPtr widget);

    [DllImport(LibGtk, EntryPoint = "gtk_container_add")]
    private static extern void gtk_container_add(IntPtr container, IntPtr widget);

    [DllImport(LibGtk, EntryPoint = "gtk_widget_destroy")]
    private static extern void gtk_widget_destroy(IntPtr widget);

    [DllImport(LibGtk, EntryPoint = "gtk_widget_get_window")]
    private static extern IntPtr gtk_widget_get_window(IntPtr widget);

    [DllImport(LibGtk, EntryPoint = "gtk_widget_set_size_request")]
    private static extern void gtk_widget_set_size_request(IntPtr widget, int width, int height);

    [DllImport(LibGtk, EntryPoint = "gtk_widget_queue_resize")]
    private static extern void gtk_widget_queue_resize(IntPtr widget);

    [DllImport(LibGtk, EntryPoint = "gtk_widget_add_events")]
    private static extern void gtk_widget_add_events(IntPtr widget, int events);

    [DllImport(LibGtk, EntryPoint = "gtk_widget_set_can_focus")]
    private static extern void gtk_widget_set_can_focus(IntPtr widget, [MarshalAs(UnmanagedType.I1)] bool canFocus);

    [DllImport(LibGtk, EntryPoint = "gtk_widget_grab_focus")]
    private static extern void gtk_widget_grab_focus(IntPtr widget);

    [DllImport(LibGObject, EntryPoint = "g_signal_connect_data")]
    private static extern ulong g_signal_connect_data(
        IntPtr instance, string detailedSignal, Delegate cHandler,
        IntPtr data, IntPtr destroyData, int connectFlags);

    [DllImport(LibCairo, EntryPoint = "cairo_image_surface_get_data")]
    private static extern IntPtr cairo_image_surface_get_data(IntPtr surface);

    [DllImport(LibCairo, EntryPoint = "cairo_image_surface_get_stride")]
    private static extern int cairo_image_surface_get_stride(IntPtr surface);

    [DllImport(LibCairo, EntryPoint = "cairo_image_surface_get_width")]
    private static extern int cairo_image_surface_get_width(IntPtr surface);

    [DllImport(LibCairo, EntryPoint = "cairo_image_surface_get_height")]
    private static extern int cairo_image_surface_get_height(IntPtr surface);

    [DllImport(LibCairo, EntryPoint = "cairo_surface_flush")]
    private static extern void cairo_surface_flush(IntPtr surface);

    [DllImport(LibCairo, EntryPoint = "cairo_surface_get_type")]
    private static extern int cairo_surface_get_type(IntPtr surface);

    [DllImport(LibCairo, EntryPoint = "cairo_image_surface_create")]
    private static extern IntPtr cairo_image_surface_create(int format, int width, int height);

    [DllImport(LibCairo, EntryPoint = "cairo_create")]
    private static extern IntPtr cairo_create(IntPtr surface);

    [DllImport(LibCairo, EntryPoint = "cairo_set_source_surface")]
    private static extern void cairo_set_source_surface(IntPtr cr, IntPtr surface, double x, double y);

    [DllImport(LibCairo, EntryPoint = "cairo_paint")]
    private static extern void cairo_paint(IntPtr cr);

    [DllImport(LibCairo, EntryPoint = "cairo_destroy")]
    private static extern void cairo_destroy(IntPtr cr);

    [DllImport(LibCairo, EntryPoint = "cairo_surface_destroy")]
    private static extern void cairo_surface_destroy(IntPtr surface);

    [DllImport(LibGtk, EntryPoint = "gtk_widget_get_allocated_width")]
    private static extern int gtk_widget_get_allocated_width(IntPtr widget);

    [DllImport(LibGtk, EntryPoint = "gtk_widget_get_allocated_height")]
    private static extern int gtk_widget_get_allocated_height(IntPtr widget);

    private const int CairoSurfaceTypeImage = 0;
    private const int CairoFormatArgb32 = 0;

    [DllImport(LibGdk, EntryPoint = "gdk_event_new")]
    private static extern IntPtr gdk_event_new(int eventType);

    [DllImport(LibGdk, EntryPoint = "gdk_event_free")]
    private static extern void gdk_event_free(IntPtr gdkEvent);

    [DllImport(LibGObject, EntryPoint = "g_object_ref")]
    private static extern IntPtr g_object_ref(IntPtr instance);

    [DllImport(LibGdk, EntryPoint = "gdk_device_get_associated_device")]
    private static extern IntPtr gdk_device_get_associated_device(IntPtr device);

    [DllImport(LibGdk, EntryPoint = "gdk_event_put")]
    private static extern void gdk_event_put(IntPtr gdkEvent);

    [DllImport(LibGdk, EntryPoint = "gdk_device_manager_get_client_pointer")]
    private static extern IntPtr gdk_device_manager_get_client_pointer(IntPtr deviceManager);

    [DllImport(LibGdk, EntryPoint = "gdk_display_get_device_manager")]
    private static extern IntPtr gdk_display_get_device_manager(IntPtr display);

    [DllImport(LibGdk, EntryPoint = "gdk_window_get_display")]
    private static extern IntPtr gdk_window_get_display(IntPtr window);

    [DllImport(LibWebKit, EntryPoint = "webkit_web_view_new")]
    private static extern IntPtr webkit_web_view_new();

    [DllImport(LibWebKit, CharSet = CharSet.Ansi, EntryPoint = "webkit_web_view_load_uri")]
    private static extern void webkit_web_view_load_uri(IntPtr webView, string uri);

    [DllImport(LibWebKit, EntryPoint = "webkit_web_view_go_back")]
    private static extern void webkit_web_view_go_back(IntPtr webView);

    [DllImport(LibWebKit, EntryPoint = "webkit_web_view_go_forward")]
    private static extern void webkit_web_view_go_forward(IntPtr webView);

    [DllImport(LibWebKit, EntryPoint = "webkit_web_view_reload")]
    private static extern void webkit_web_view_reload(IntPtr webView);

    [DllImport(LibWebKit, EntryPoint = "webkit_web_view_stop_loading")]
    private static extern void webkit_web_view_stop_loading(IntPtr webView);

    [DllImport(LibWebKit, EntryPoint = "webkit_web_view_can_go_back")]
    private static extern int webkit_web_view_can_go_back(IntPtr webView);

    [DllImport(LibWebKit, EntryPoint = "webkit_web_view_can_go_forward")]
    private static extern int webkit_web_view_can_go_forward(IntPtr webView);

    [DllImport(LibWebKit, EntryPoint = "webkit_web_view_is_loading")]
    private static extern int webkit_web_view_is_loading(IntPtr webView);

    [DllImport(LibWebKit, EntryPoint = "webkit_web_view_get_uri")]
    private static extern IntPtr webkit_web_view_get_uri(IntPtr webView);

    [DllImport(LibWebKit, EntryPoint = "webkit_navigation_policy_decision_get_navigation_action")]
    private static extern IntPtr webkit_navigation_policy_decision_get_navigation_action(IntPtr decision);

    [DllImport(LibWebKit, EntryPoint = "webkit_navigation_action_get_request")]
    private static extern IntPtr webkit_navigation_action_get_request(IntPtr navigationAction);

    [DllImport(LibWebKit, EntryPoint = "webkit_uri_request_get_uri")]
    private static extern IntPtr webkit_uri_request_get_uri(IntPtr request);

    [DllImport(LibWebKit, EntryPoint = "webkit_policy_decision_ignore")]
    private static extern void webkit_policy_decision_ignore(IntPtr decision);

    // Available since WebKitGTK 2.28. This is a borrowed pointer owned by
    // WebKit; it must only be used on the GTK thread and must not be freed.
    [DllImport(LibWebKit, EntryPoint = "webkit_web_view_get_input_method_context")]
    private static extern IntPtr webkit_web_view_get_input_method_context(IntPtr webView);

    [DllImport(LibGObject, EntryPoint = "g_signal_emit_by_name",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_signal_emit_by_name_committed(
        IntPtr instance,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string signal,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [DllImport(LibWebKit, EntryPoint = "webkit_web_view_get_settings")]
    private static extern IntPtr webkit_web_view_get_settings(IntPtr webView);

    [DllImport(LibWebKit, EntryPoint = "webkit_settings_set_hardware_acceleration_policy")]
    private static extern void webkit_settings_set_hardware_acceleration_policy(IntPtr settings, int policy);
    
    [DllImport(LibGdk, EntryPoint = "gdk_event_set_device")]
    private static extern void gdk_event_set_device(IntPtr gdkEvent, IntPtr device);

    private const int WebKitHardwareAccelerationPolicyNever = 2;

    #endregion

}
