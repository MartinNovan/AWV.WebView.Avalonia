namespace AWV.WebView.Avalonia;

/// <summary>
/// Thread-safe policy for navigation destinations (not network requests).
/// Use exact hostnames or a leading '*.' to cover subdomains, e.g.
/// 'example.org' and '*.example.org'. A nonempty allowlist restricts remote
/// navigation; blocked hosts always take precedence. The rules apply to
/// navigations, including redirects when the platform reports them, but DO
/// NOT filter page subresources, cookies, JavaScript fetches or frame traffic.
/// This is not a web security sandbox or a content blocker.
/// </summary>
public sealed class WebViewNavigationPolicy
{
    private readonly object _gate = new();
    private string[] _allowed = [];
    private string[] _blocked = [];
    private bool _allowLocalFiles = true;
    private bool _allowDataUrls = true;
    private bool _allowContentUrls = true;

    /// <summary>If false, block file:// navigations (default true).</summary>
    public bool AllowLocalFiles
    {
        get { lock (_gate) return _allowLocalFiles; }
        set { lock (_gate) _allowLocalFiles = value; }
    }

    /// <summary>If false, block data: navigations (default true).</summary>
    public bool AllowDataUrls
    {
        get { lock (_gate) return _allowDataUrls; }
        set { lock (_gate) _allowDataUrls = value; }
    }

    /// <summary>If false, block Android content: URIs (default true).</summary>
    public bool AllowContentUrls
    {
        get { lock (_gate) return _allowContentUrls; }
        set { lock (_gate) _allowContentUrls = value; }
    }

    /// <summary>A snapshot of the currently allowed host patterns.</summary>
    public IReadOnlyList<string> AllowedHosts { get { lock (_gate) return Array.AsReadOnly((string[])_allowed.Clone()); } }

    /// <summary>A snapshot of the currently blocked host patterns.</summary>
    public IReadOnlyList<string> BlockedHosts { get { lock (_gate) return Array.AsReadOnly((string[])_blocked.Clone()); } }

    /// <summary>Replaces the remote-host allowlist; empty means all HTTP(S) hosts are allowed.</summary>
    public void SetAllowedHosts(params string[] hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        var patterns = NormalizePatterns(hosts);
        lock (_gate) _allowed = patterns;
    }

    /// <summary>Replaces the remote-host blocklist.</summary>
    public void SetBlockedHosts(params string[] hosts)
    {
        ArgumentNullException.ThrowIfNull(hosts);
        var patterns = NormalizePatterns(hosts);
        lock (_gate) _blocked = patterns;
    }

    /// <summary>Checks an absolute navigation URI. For local file paths, use WebView.Navigate instead.</summary>
    public bool IsAllowed(string url) => IsAllowed(url, out _);

    /// <summary>Checks an absolute URI and returns an explanation for rejection.</summary>
    public bool IsAllowed(string url, out string reason)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            reason = "Invalid or relative URI";
            return false;
        }

        lock (_gate)
        {
            switch (uri.Scheme.ToLowerInvariant())
            {
                case "about" when uri.OriginalString.Equals("about:blank", StringComparison.OrdinalIgnoreCase):
                    reason = "";
                    return true;
                case "file":
                    reason = _allowLocalFiles ? "" : "Local file navigation disabled";
                    return _allowLocalFiles;
                case "data":
                    reason = _allowDataUrls ? "" : "Data URL navigation disabled";
                    return _allowDataUrls;
                case "content":
                    reason = _allowContentUrls ? "" : "Content URI navigation disabled";
                    return _allowContentUrls;
                case "http":
                case "https":
                    var host = uri.IdnHost.TrimEnd('.');
                    if (_blocked.Any(pattern => Matches(host, pattern)))
                    {
                        reason = "Blocked host";
                        return false;
                    }
                    if (_allowed.Length > 0 && !_allowed.Any(pattern => Matches(host, pattern)))
                    {
                        reason = "Host not in allowlist";
                        return false;
                    }
                    reason = "";
                    return true;
                default:
                    // Explicitly reject unhandled external/custom schemes,
                    // including javascript:, intent:, mailto:, and app links.
                    reason = "Unsupported navigation scheme";
                    return false;
            }
        }
    }

    private static string[] NormalizePatterns(IEnumerable<string> hosts) =>
        hosts.Select(NormalizePattern).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static string NormalizePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("Host patterns must not be empty.", nameof(pattern));
        var trimmed = pattern.Trim().TrimEnd('.').ToLowerInvariant();
        var wildcard = trimmed.StartsWith("*.", StringComparison.Ordinal);
        var hostname = wildcard ? trimmed[2..] : trimmed;
        if (hostname.Length == 0 || hostname.Contains('/') || hostname.Contains(':') || hostname.Contains('*') ||
            Uri.CheckHostName(hostname) == UriHostNameType.Unknown)
            throw new ArgumentException($"Invalid host pattern '{pattern}'. Use a hostname or *.hostname.", nameof(pattern));
        return wildcard ? "*." + hostname : hostname;
    }

    private static bool Matches(string host, string pattern) =>
        pattern.StartsWith("*.", StringComparison.Ordinal) 
            ? host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase) && host.Length > pattern.Length - 1
            : host.Equals(pattern, StringComparison.OrdinalIgnoreCase);
}
