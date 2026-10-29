using System.Runtime.InteropServices;
using System.Text;

namespace AWV.WebView.Avalonia.Managers;

/// <summary>
/// Forwards Avalonia input as GTK3 events or WebKit IME commits.
/// </summary>
internal static partial class GtkManager
{
    private const int GdkMotionNotify = 3;
    private const int GdkButtonPress = 4;
    private const int GdkButtonRelease = 7;
    private const int GdkKeyPress = 8;
    private const int GdkKeyRelease = 9;
    private const int GdkScroll = 31;
    private const int GdkScrollSmooth = 4;

    private const uint GdkShiftMask = 1 << 0;
    private const uint GdkControlMask = 1 << 2;
    private const uint GdkAltMask = 1 << 3;
    private const uint GdkMod5Mask = 1 << 7; 
    private const uint GdkButton1Mask = 1 << 8;
    private const uint GdkButton2Mask = 1 << 9;
    private const uint GdkButton3Mask = 1 << 10;

    private const int GdkPointerMotionMask = 1 << 2;
    private const int GdkButtonPressMask = 1 << 8;
    private const int GdkButtonReleaseMask = 1 << 9;
    private const int GdkScrollMask = 1 << 4;
    private const int GdkKeyPressMask = 1 << 10;
    private const int GdkKeyReleaseMask = 1 << 11;

    private const uint GdkKeyShiftL = 0xffe1;
    private const uint GdkKeyShiftR = 0xffe2;
    private const uint GdkKeyControlL = 0xffe3;
    private const uint GdkKeyControlR = 0xffe4;
    private const uint GdkKeyAltL = 0xffe9;
    private const uint GdkKeyAltR = 0xffea;
    private const uint GdkKeyIsoLevel3Shift = 0xfe03;

    [DllImport(LibGlib, EntryPoint = "g_strdup")]
    private static extern IntPtr g_strdup([MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [DllImport(LibGdk, EntryPoint = "gdk_unicode_to_keyval")]
    private static extern uint gdk_unicode_to_keyval(uint wc);

    [DllImport(LibGdk, EntryPoint = "gdk_keymap_get_default")]
    private static extern IntPtr gdk_keymap_get_default();

    [DllImport(LibGdk, EntryPoint = "gdk_keymap_get_entries_for_keyval")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool gdk_keymap_get_entries_for_keyval(IntPtr keymap, uint keyval, out IntPtr keys, out int nKeys);

    [DllImport(LibGlib, EntryPoint = "g_free")]
    private static extern void g_free(IntPtr mem);

    [StructLayout(LayoutKind.Sequential)]
    private struct GdkKeymapKey
    {
        public uint keycode;
        public int group;
        public int level;
    }

    private static (ushort Keycode, byte Group) ResolveKeycode(uint keyval)
    {
        var keymap = gdk_keymap_get_default();
        if (keymap == IntPtr.Zero)
            return (0, 0);

        IntPtr keysPtr = IntPtr.Zero;
        try
        {
            if (!gdk_keymap_get_entries_for_keyval(keymap, keyval, out keysPtr, out var count)
                || count <= 0 || keysPtr == IntPtr.Zero)
                return (0, 0);

            var first = Marshal.PtrToStructure<GdkKeymapKey>(keysPtr);
            return ((ushort)first.keycode, (byte)first.group);
        }
        finally
        {
            if (keysPtr != IntPtr.Zero)
                g_free(keysPtr);
        }
    }

    /// <summary>
    /// Keeps at most one pending pointer-motion item per view. GTK dispatch
    /// may lag a fast mouse; only the newest coordinates are useful then.
    /// </summary>
    public static void SendPointerMotion(OffscreenWebView ov, double x, double y)
    {
        if (ov.IsDestroyed) return;

        lock (ov.MotionGate)
        {
            ov.LastMotionX = x;
            ov.LastMotionY = y;
            if (ov.MotionQueued) return;
            ov.MotionQueued = true;
        }

        PostToGtkThread(() =>
        {
            double currentX;
            double currentY;
            lock (ov.MotionGate)
            {
                currentX = ov.LastMotionX;
                currentY = ov.LastMotionY;
                ov.MotionQueued = false;
            }

            if (ov.IsDestroyed) return;
            var window = gtk_widget_get_window(ov.WebView);
            if (window == IntPtr.Zero) return;

            var evt = new GdkEventMotion
            {
                type = GdkMotionNotify,
                window = window,
                send_event = 1,
                time = GetEventTime(),
                x = currentX,
                y = currentY,
                axes = IntPtr.Zero,
                state = ov.ModifierState | ov.PointerState,
                is_hint = 0,
                device = GetClientPointerDevice(window),
                x_root = currentX,
                y_root = currentY
            };
            PutEvent(ref evt, window);
        });
    }

    public static void SendPointerButton(OffscreenWebView ov, double x, double y, uint button, bool press)
    {
        if (ov.IsDestroyed) return;
        PostToGtkThread(() =>
        {
            if (ov.IsDestroyed) return;
            var window = gtk_widget_get_window(ov.WebView);
            if (window == IntPtr.Zero) return;

            if (press)
                gtk_widget_grab_focus(ov.WebView);

            var evt = new GdkEventButton
            {
                type = press ? GdkButtonPress : GdkButtonRelease,
                window = window,
                send_event = 1,
                time = GetEventTime(),
                x = x,
                y = y,
                axes = IntPtr.Zero,
                state = ov.ModifierState | ov.PointerState,
                button = button,
                device = GetClientPointerDevice(window),
                x_root = x,
                y_root = y
            };
            PutEvent(ref evt, window);

            var mask = ButtonMask(button);
            if (press)
                ov.PointerState |= mask;
            else
                ov.PointerState &= ~mask;
        });
    }

    public static void SendScroll(OffscreenWebView ov, double x, double y, double deltaX, double deltaY)
    {
        if (ov.IsDestroyed) return;
        PostToGtkThread(() =>
        {
            if (ov.IsDestroyed) return;
            var window = gtk_widget_get_window(ov.WebView);
            if (window == IntPtr.Zero) return;

            var evt = new GdkEventScroll
            {
                type = GdkScroll,
                window = window,
                send_event = 1,
                time = GetEventTime(),
                x = x,
                y = y,
                state = ov.ModifierState | ov.PointerState,
                direction = GdkScrollSmooth,
                device = GetClientPointerDevice(window),
                x_root = x,
                y_root = y,
                delta_x = deltaX,
                delta_y = -deltaY
            };
            PutEvent(ref evt, window);
        });
    }

    public static void SendKey(OffscreenWebView ov, uint keyval, bool press)
    {
        if (keyval != 0 && !ov.IsDestroyed)
            PostToGtkThread(() =>
            {
                if (!ov.IsDestroyed)
                    SendKeyOnGtkThread(ov, keyval, press);
            });
    }

    private static void SendKeyOnGtkThread(OffscreenWebView ov, uint keyval, bool press,
        string? textInput = null)
    {
        if (ov.IsDestroyed) return;
        var window = gtk_widget_get_window(ov.WebView);
        if (window == IntPtr.Zero) return;

        var (hwKeycode, group) = ResolveKeycode(keyval);
        var isText = textInput != null;
        var evt = new GdkEventKey
        {
            type = press ? GdkKeyPress : GdkKeyRelease,
            window = window,
            send_event = 1,
            time = GetEventTime(),
            state = isText ? 0u : ov.ModifierState,
            keyval = keyval,
            length = isText && press ? Encoding.UTF8.GetByteCount(textInput!) : 0,
            str = isText && press ? g_strdup(textInput!) : IntPtr.Zero,
            hardware_keycode = hwKeycode,
            group = group,
            is_modifier = 0
        };
        PutEvent(ref evt, window);
        if (!isText)
            UpdateModifierState(ov, keyval, press);
    }

    public static void SendUnicodeText(OffscreenWebView ov, string text)
    {
        if (string.IsNullOrEmpty(text) || ov.IsDestroyed) return;
        PostToGtkThread(() =>
        {
            if (ov.IsDestroyed || ov.WebView == IntPtr.Zero) return;

            var context = webkit_web_view_get_input_method_context(ov.WebView);
            if (context != IntPtr.Zero)
            {
                g_signal_emit_by_name_committed(context, "committed", text);
                if (InputDebug)
                    Console.Error.WriteLine($"[AWV GTK] IM committed: {text}");
                return;
            }

            if (InputDebug)
                Console.Error.WriteLine("[AWV GTK] no WebKit input method context; using GDK text fallback");
            foreach (var rune in text.EnumerateRunes())
            {
                var keyval = gdk_unicode_to_keyval((uint)rune.Value);
                if (keyval == 0) continue;
                var character = rune.ToString();
                SendKeyOnGtkThread(ov, keyval, true, character);
                SendKeyOnGtkThread(ov, keyval, false, character);
            }
        });
    }

    private static readonly bool InputDebug =
        Environment.GetEnvironmentVariable("AWV_INPUT_DEBUG") == "1";

    private static uint GetEventTime() => unchecked((uint)Environment.TickCount);

    private static int _loggedGdkAllocatorVersion;

    private static void PutEvent<T>(ref T evt, IntPtr window) where T : struct
    {
        var eventType = evt switch
        {
            GdkEventMotion e => e.type,
            GdkEventButton e => e.type,
            GdkEventScroll e => e.type,
            GdkEventKey e => e.type,
            GdkEventFocus e => e.type,
            _ => throw new NotSupportedException($"Unsupported GdkEvent: {typeof(T).Name}")
        };

#if DEBUG
        if (Interlocked.Exchange(ref _loggedGdkAllocatorVersion, 1) == 0)
            Console.Error.WriteLine("[AWV] GDK events allocated with gdk_event_new/gdk_event_free");
#endif
        var ptr = gdk_event_new(eventType);
        if (ptr == IntPtr.Zero)
            throw new InvalidOperationException("gdk_event_new failed");

        try
        {
            if (window != IntPtr.Zero)
                g_object_ref(window);

            Marshal.StructureToPtr(evt, ptr, false);

            var device = GetClientPointerDevice(window);
            if (device != IntPtr.Zero)
            {
                if (eventType is GdkKeyPress or GdkKeyRelease)
                {
                    var keyboard = gdk_device_get_associated_device(device);
                    if (keyboard != IntPtr.Zero)
                        device = keyboard;
                }
                gdk_event_set_device(ptr, device);
            }

            gdk_event_put(ptr);
        }
        finally
        {
            gdk_event_free(ptr);
        }
    }

    private static IntPtr GetClientPointerDevice(IntPtr window)
    {
        var display = gdk_window_get_display(window);
        if (display == IntPtr.Zero)
            return IntPtr.Zero;

        var manager = gdk_display_get_device_manager(display);
        if (manager == IntPtr.Zero)
            return IntPtr.Zero;

        return gdk_device_manager_get_client_pointer(manager);
    }

    private static uint ButtonMask(uint button) => button switch
    {
        1 => GdkButton1Mask,
        2 => GdkButton2Mask,
        3 => GdkButton3Mask,
        _ => 0
    };

    private static void UpdateModifierState(OffscreenWebView ov, uint keyval, bool press)
    {
        uint mask = keyval switch
        {
            GdkKeyShiftL or GdkKeyShiftR => GdkShiftMask,
            GdkKeyControlL or GdkKeyControlR => GdkControlMask,
            GdkKeyAltL or GdkKeyAltR => GdkAltMask,
            GdkKeyIsoLevel3Shift => GdkMod5Mask,
            _ => 0
        };

        if (mask == 0)
            return;

        if (press)
            ov.ModifierState |= mask;
        else
            ov.ModifierState &= ~mask;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdkEventFocus
    {
        public int type;
        public IntPtr window;
        public sbyte send_event;
        public short in_;
    }

    private const int GdkFocusChange = 12;

    public static void SendFocus(OffscreenWebView ov, bool focusIn)
    {
        if (ov.IsDestroyed) return;
        PostToGtkThread(() =>
        {
            if (ov.IsDestroyed) return;
            var window = gtk_widget_get_window(ov.WebView);
            if (window == IntPtr.Zero) return;

            if (focusIn)
                gtk_widget_grab_focus(ov.WebView);

            var evt = new GdkEventFocus
            {
                type = GdkFocusChange,
                window = window,
                send_event = 1,
                in_ = (short)(focusIn ? 1 : 0)
            };
            PutEvent(ref evt, window);
        });
    }
    
    #region GDK event structs (GTK3, gdk/gdkevents.h)

    [StructLayout(LayoutKind.Sequential)]
    private struct GdkEventMotion
    {
        public int type;
        public IntPtr window;
        public sbyte send_event;
        public uint time;
        public double x;
        public double y;
        public IntPtr axes;
        public uint state;
        public short is_hint;
        public IntPtr device;
        public double x_root;
        public double y_root;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdkEventButton
    {
        public int type;
        public IntPtr window;
        public sbyte send_event;
        public uint time;
        public double x;
        public double y;
        public IntPtr axes;
        public uint state;
        public uint button;
        public IntPtr device;
        public double x_root;
        public double y_root;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdkEventScroll
    {
        public int type;
        public IntPtr window;
        public sbyte send_event;
        public uint time;
        public double x;
        public double y;
        public uint state;
        public int direction;
        public IntPtr device;
        public double x_root;
        public double y_root;
        public double delta_x;
        public double delta_y;
        public uint is_stop;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdkEventKey
    {
        public int type;
        public IntPtr window;
        public sbyte send_event;
        public uint time;
        public uint state;
        public uint keyval;
        public int length;
        public IntPtr str;
        public ushort hardware_keycode;
        public byte group;
        public uint is_modifier; 
    }

    #endregion
}
