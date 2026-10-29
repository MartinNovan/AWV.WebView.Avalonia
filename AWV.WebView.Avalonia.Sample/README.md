# AWV.WebView.Avalonia sample: browser navigation

This is a **consumer-built UI** demonstrating the public navigation API from AWV v8. No browser chrome is built into the library.

## Run

From the repository root:

```bash
# Linux (Wayland or X11), .NET 9 SDK and system WebKitGTK 4.1 / GTK 3 required
dotnet run --project samples/AWV.WebView.Avalonia.Sample

# Windows: the project automatically targets net9.0-windows and uses WebView2
# Use the same command in a Windows terminal with the WebView2 runtime installed.
```

The sample references the AWV library source project. Its target framework is selected by the build OS, so Windows uses `net9.0-windows` rather than accidentally selecting the Linux implementation from `net9.0`.

## Demo features

- **Back**, **Forward**, **Reload**, **Stop** and **Home** call `IWebViewNavigation` methods.
- The **address bar** accepts HTTP(S) links and local paths. Press Enter or click Go.
- Status fields are updated via `NavigationStateChanged`; `CurrentUrl` reflects redirects/history, unlike the requested `Url`.
- **Navigation policy** accepts comma-separated exact hosts and wildcard subdomains. Blocked hosts override allowed hosts.
- Toggle **Allow local file pages** to test the `file://` rule.
- **Open local history test** opens two bundled offline HTML pages; navigation and history work without internet access.
- **NavigationBlocked** reports rejected destinations in the status bar.

### Important limits

The navigation policy controls supported page navigations, **not every network request**. It is not a sandbox, network firewall, content blocker or malware defence. Linux uses GTK offscreen rendering; video players such as YouTube are known to be unstable on some systems. The sample starts with a lightweight site intentionally.

The sample is a desktop example only. AWV's Android backend is part of the library but needs a separate Android application entry point.
