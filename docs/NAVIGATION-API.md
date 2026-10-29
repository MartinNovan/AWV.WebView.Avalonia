# AWV navigation API

AWV provides browser navigation **logic**, not a toolbar. Users supply their own Avalonia controls. `WebView` implements `IWebViewNavigation`; navigation state properties are read-only Avalonia `DirectProperty` values, so they can be bound from XAML.

## Example

```xml
<Grid RowDefinitions="Auto,*">
    <StackPanel Orientation="Horizontal" Spacing="8">
        <Button Content="←" IsEnabled="{Binding #Browser.CanGoBack}" Click="BackClicked" />
        <Button Content="→" IsEnabled="{Binding #Browser.CanGoForward}" Click="ForwardClicked" />
        <Button Content="↻" Click="ReloadClicked" />
    </StackPanel>
    <awv:WebView x:Name="Browser" Grid.Row="1" Url="https://docs.example.org"/>
</Grid>
```

Alternatively, access the view using `IWebViewNavigation` in code:

```csharp
IWebViewNavigation browser = Browser;
browser.NavigationPolicy.SetAllowedHosts("docs.example.org", "*.docs.example.org");
browser.NavigationPolicy.SetBlockedHosts("tracking.example.org");

browser.NavigationBlocked += (_, args) =>
    Console.WriteLine($"Blocked {args.Url}: {args.Reason}");
browser.NavigationStateChanged += (_, _) =>
    Console.WriteLine($"{browser.CurrentUrl}, loading={browser.IsLoading}");

browser.Navigate("https://docs.example.org/start");
browser.GoBack();
browser.GoForward();
browser.Reload();
browser.Stop();
```

The actual URL after redirects/history is `CurrentUrl`; `Url` remains the requested URL, so binding `Url` back to `CurrentUrl` can create unwanted navigation loops. `CanGoBack`, `CanGoForward`, `IsLoading` and `CurrentUrl` update on the Avalonia UI thread.

Configure the policy **before** the initial URL is loaded if you want to restrict that initial navigation (e.g. in the window constructor before it is attached).

## Host patterns

- `example.com` matches **only** `example.com` (not `sub.example.com`).
- `*.example.com` matches subdomains such as `a.example.com` (not the root).
- Empty allowed-host list means all HTTP(S) hosts may be navigated to.
- The blocked-host list is always applied first, even if the host is also allowed.
- Hosts must be provided without schemes, ports, paths or query strings.
- Built-in rules allow `about:blank`, local files, `data:` and Android `content:` URLs unless disabled via `AllowLocalFiles`, `AllowDataUrls` and `AllowContentUrls`. Those schemes are **not** subject to host allowlists.
- Other schemes (`javascript:`, `mailto:`, `intent:`, etc.) are not navigated within the WebView.
- New windows / `target=_blank` are blocked by default so they cannot escape host navigation rules.

## Limits and platform behavior

This is a **navigation filter**, *not* a network interception layer or content-security boundary. It does not filter page subresources (images, scripts, XHR/fetch), downloads, service workers or cross-origin requests. Embedded frames and navigation types differ between engines. Do not use this to protect against malicious pages or to replace a sandbox/CSP/network-level filtering.

Windows WebView2 checks top-level navigations including HTTP redirects. Linux WebKitGTK checks navigation policy decisions, including link clicks/redirects and some subframe transitions. Android WebViewClient checks reported main-frame link navigations/redirects but cannot intercept all navigations, including some POST requests.

Use custom UI on Avalonia's UI thread; the library relays status events back onto that thread. The native hosts' callbacks are not exposed to consumers.
