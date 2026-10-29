using Avalonia.Input;

namespace AWV.WebView.Avalonia.Managers;

// Maps Avalonia keys to GTK3 keyvals. Printable characters are normally
// delivered through TextInput; A-Z and digits are used for Ctrl/Alt shortcuts.
//
// Printable characters (letters, digits, punctuation, space) are deliberately
// NOT handled here anymore - they go through WebView's OnTextInput handler
// instead, which Avalonia already resolves according to whatever keyboard
// layout is actually active system-wide. Mapping Key.A -> 'a' etc. here would
// hardcode a US layout regardless of what the user has configured, and if you
// map it here, it would need to fire it AGAIN when the same character also
// comes through OnTextInput - so don't add printable keys back to this table.
internal static class GdkKeyMap
{
    public static uint ToGdkKeyval(Key key)
    {
        if (key is >= Key.F1 and <= Key.F12)
            return (uint)(0xffbe + (key - Key.F1)); // GDK_KEY_F1 == 0xffbe

        if (key is >= Key.A and <= Key.Z)
            return (uint)(0x0061 + (key - Key.A)); // GDK_KEY_a je 0x0061
            
        if (key is >= Key.D0 and <= Key.D9)
            return (uint)(0x0030 + (key - Key.D0)); // GDK_KEY_0 je 0x0030
        
        return key switch
        {
            Key.Back => 0xff08,          // GDK_KEY_BackSpace
            Key.Tab => 0xff09,           // GDK_KEY_Tab
            Key.Return or Key.Enter => 0xff0d, // GDK_KEY_Return
            Key.Escape => 0xff1b,        // GDK_KEY_Escape
            Key.Delete => 0xffff,        // GDK_KEY_Delete
            Key.Home => 0xff50,
            Key.Left => 0xff51,
            Key.Up => 0xff52,
            Key.Right => 0xff53,
            Key.Down => 0xff54,
            Key.PageUp => 0xff55,
            Key.PageDown => 0xff56,
            Key.End => 0xff57,
            Key.Insert => 0xff63,
            Key.LeftShift => 0xffe1, // Shift_L
            Key.RightShift => 0xffe2, // Shift_R
            Key.LeftCtrl => 0xffe3, // Control_L
            Key.RightCtrl => 0xffe4, // Control_R
            Key.LeftAlt => 0xffe9, // Alt_L
            Key.RightAlt => 0xfe03, // ISO_Level3_Shift (AltGr on common European layouts)

            _ => 0
        };
    }
}