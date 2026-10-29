# AWV.WebView.Avalonia

**Cross-platform WebView for Avalonia UI — Windows, Linux (X11/Wayland), and Android.**

---
AWV embeds a platform web engine in your Avalonia app. It provides a WebView control, navigation and state APIs, and URL allow/block rules **without imposing any browser UI**. Bring your own toolbar, address bar, and styling.

> **Preview release.** Windows, Linux and Android have been exercised in the sample app. Linux uses experimental offscreen WebKitGTK rendering; HTML5 video/streaming can stall or freeze the application. macOS is not implemented.

[NuGet package](https://www.nuget.org/packages/AWV.WebView.Avalonia) · [Source code](https://github.com/MartinNovan/AWV.WebView.Avalonia) · [Issues](https://github.com/MartinNovan/AWV.WebView.Avalonia/issues) · [License](https://github.com/MartinNovan/AWV.WebView.Avalonia/blob/master/LICENSE)

## Features

- **Windows:** Microsoft Edge WebView2.
- **Linux:** WebKitGTK 4.1 / GTK3 using an offscreen surface (X11 and Wayland support).
- **Android:** Android System WebView hosted as a native Avalonia view.
- **Navigation API:** Navigate, Back, Forward, Reload, Stop, loading state, current URL and history availability.
- **Navigation rules:** allowed/blocked hostname patterns and a blocked-navigation event.
- **Local pages:** `file://` URIs and local filesystem paths where supported by the platform.
- **Custom UI:** no built-in address bar or navigation buttons.

## Installation

```bash
dotnet add package AWV.WebView.Avalonia
```

If you use central package management, add the package version to `Directory.Packages.props` and reference it without a version from your project. You can also use a `ProjectReference` during development.

### Platform prerequisites

| Platform | Application target framework | Requirements |
| --- | --- | --- |
| Windows | `net9.0-windows` | Microsoft Edge WebView2 Runtime; set `<UseWindowsForms>true</UseWindowsForms>` in the Windows launcher |
| Linux | `net9.0` | GTK3, Cairo, WebKitGTK **4.1** (`libwebkit2gtk-4.1.so.0`) |
| Android | `net9.0-android` | Android System WebView; `android.permission.INTERNET` for remote pages |

On Arch Linux:

```bash
sudo pacman -S gtk3 webkit2gtk-4.1
```

On Ubuntu 24.04 and compatible systems:

```bash
sudo apt install libgtk-3-0t64 libwebkit2gtk-4.1-0
```

Install native Linux libraries separately: they are **not** bundled in the NuGet package. For Android, ensure your launcher has `INTERNET` permission in its manifest if loading network URLs:

```xml
<uses-permission android:name="android.permission.INTERNET" />
```

## Quick start

Add the XML namespace and place the control anywhere in your Avalonia layout:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:awv="clr-namespace:AWV.WebView.Avalonia;assembly=AWV.WebView.Avalonia">
    <awv:WebView x:Name="Browser" Url="https://example.com" />
</UserControl>
```

`Url` is an Avalonia styled property and supports binding. `CurrentUrl` reports the actual URL after redirects and history navigation; it is not necessarily the same as the requested `Url`.

### Navigation from your own UI

`WebView` implements `IWebViewNavigation`. For example, in the view's code-behind (with `Browser` named in XAML):

```csharp
using AWV.WebView.Avalonia;

IWebViewNavigation navigation = Browser;

navigation.Navigate("https://example.com");
navigation.GoBack();
navigation.GoForward();
navigation.Reload();
navigation.Stop();

navigation.NavigationStateChanged += (_, _) =>
{
    // Raised on Avalonia's UI thread.
    Console.WriteLine($"{navigation.CurrentUrl} | loading={navigation.IsLoading}");
    Console.WriteLine($"Back={navigation.CanGoBack}, Forward={navigation.CanGoForward}");
};
```

The library does not render navigation buttons. Bind your own button's `IsEnabled` to the WebView's `CanGoBack` / `CanGoForward`, or update your UI from `NavigationStateChanged`.

### Allowed and blocked websites

```csharp
// Configure before navigating to restricted content.
Browser.NavigationPolicy.SetAllowedHosts("example.com", "*.example.com");
Browser.NavigationPolicy.SetBlockedHosts("ads.example.com");
Browser.NavigationPolicy.AllowLocalFiles = false;

Browser.NavigationBlocked += (_, e) =>
    Console.WriteLine($"Navigation denied: {e.Url} ({e.Reason})");
```

A host like `example.com` matches only the exact host; `*.example.com` matches its subdomains. An empty allowlist allows all HTTP(S) hosts; the blocklist always takes precedence.

**Security note:** This is a **top-level navigation filter**, not a firewall, content blocker, CSP or sandbox. It does not reliably intercept all requests, subresources, JavaScript fetches, or every type of navigation on every engine. See [Navigation API documentation](https://github.com/MartinNovan/AWV.WebView.Avalonia/blob/master/docs/NAVIGATION-API.md).

### Local HTML files

```csharp
Browser.Navigate("file:///home/user/site/index.html");
// Or an absolute path on supported desktop platforms:
Browser.Navigate("/home/user/site/index.html");
```

Relative paths are resolved against `AppContext.BaseDirectory`, **not** your solution/source folder. Copy HTML into the application output/publish directory, or resolve an absolute path. Android requires platform-accessible files or resources; desktop filesystem paths do not work there.

## Known limitations

- **Linux video:** WebKitGTK inside `GtkOffscreenWindow` may not play streaming/HTML5 videos correctly; some players can freeze the process. Not recommended for video-heavy apps without testing.
- **Linux rendering:** offscreen Cairo-to-Avalonia copies may use significant CPU. Advanced GPU compositing, popups, IME, drag and drop, WebRTC, protected content and accessibility are not fully verified.
- **Navigation policies:** enforcement depends on native callbacks; some requests and navigation types are outside their scope.
- **Cross-platform behavior:** local-file access, permissions, multimedia codecs and OS WebView features differ by platform.
- **macOS:** not supported for now.

## Sample and development

The repository includes a sample browser illustrating custom navigation UI and local-page navigation. Refer to the [GitHub repository](https://github.com/MartinNovan/AWV.WebView.Avalonia) for the current sample project and platform launchers.

To build this library from source with the .NET 9 SDK and Android workload installed:

```bash
dotnet pack AWV.WebView.Avalonia/AWV.WebView.Avalonia.csproj -c Release
```

## Contributing and license

Bug reports and contributions are welcome through [GitHub Issues](https://github.com/MartinNovan/AWV.WebView.Avalonia/issues).

Licensed under **GNU General Public License v3** ([LICENSE](https://github.com/MartinNovan/AWV.WebView.Avalonia/blob/master/LICENSE)). If you distribute an application incorporating this library, review the GPL's obligations and the licenses of third-party components. This is not an MIT-licensed library.
