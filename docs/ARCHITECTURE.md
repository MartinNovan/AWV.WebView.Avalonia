# Architecture and ownership

## Common entry point

`WebView` is the public Avalonia `ContentControl`. `UrlProperty` is a styled property; it is normalized by `WebViewUrlNormalizer` for all platforms. Platform hosts and the GTK/native interop helpers are implementation details (`internal`) rather than supported public extension points.

### Windows

`NativeHost` inherits `NativeControlHost`, and `WindowsWebViewHost` creates a WebView2 WinForms control hosted in a WinForms `Panel` attached to Avalonia's HWND. The host subclasses the parent WndProc to track size changes. **Dispose flow:** the Avalonia native host calls `DestroyNativeControlCore`; the WebView2 control and panel are disposed, and the original WndProc and the delegate GC handle are restored/released.

### Android

`NativeHost` creates a `FrameLayout` with the Android WebView. JavaScript and DOM storage are enabled, and navigation stays inside the view for supported schemes. It permits local file access but disables universal file-origin access and unsafe mixed HTTP content on HTTPS pages. Applications are responsible for `INTERNET` permission and for availability/accessibility of local content.

### Linux / GTK3

```text
Avalonia input thread                         Dedicated GTK thread
---------------------                         --------------------
WebView.LinuxWebViewSurface  --commands-->    GtkManager dispatch queue
OnTextInput -> UTF-8 committed text           WebKit input method context
Key/Pointer/Scroll -> GdkEvent                GDK event queue
                                              GtkOffscreenWindow + WebKitWebView
Avalonia dispatcher          <--copy-----     Cairo surface / damage-event
WriteableBitmap -> Render()
```

GTK and WebKit objects are created, modified and destroyed on the single GTK thread. The frame callback borrows Cairo memory only during invocation; `WebView.OnLinuxFrameReady` copies the bytes before returning and posts exactly one pending bitmap update to Avalonia's dispatcher. `GtkManager` throttles frame capture to approximately 60 Hz and coalesces pointer-motion messages to prevent unbounded backlog during heavy pages.

On control detach, the wrapper unregisters `FrameReady`, marks native GTK state destroyed, drains earlier pending operations safely and destroys its parent GTK widget. Old posted Avalonia frames carry a generation number and are discarded after a backend change.

### Keyboard input

Keyboard **text** must come from `Avalonia.Input.TextInputEventArgs.Text` to follow the active system layout, IME and AltGr, rather than mapping physical keys to US characters. The Linux host commits text via WebKit's `WebKitInputMethodContext::committed` signal. Synthetic GDK keys are reserved for navigation, modifiers, shortcuts and compatibility fallback. `PhysicalKey` distinguishes right Alt from left Alt even when `Key` does not.

`ResolveKeycode` queries GDK for each shortcut/key event rather than caching a mapping across keyboard-layout changes. The native interop assumes GTK **3**, not GTK 4; see testing notes for ABI concerns.

## Error handling and limitations

- Failure to initialize GTK throws on the caller rather than killing the whole application from a background exception.
- Synchronous operations have a 10-second bound. Frame and mouse callbacks are asynchronous to avoid stalling Avalonia.
- A critical failure of the GTK loop is logged and queued synchronous operations are failed rather than waiting indefinitely.
- Linux WebKitGTK is tied to the distro's installed native libraries; the NuGet package does not bundle them.
- GTK3 offscreen callbacks are experimental. Popups, accessibility, video and advanced IME should be validated separately.
