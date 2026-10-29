namespace AWV.WebView.Avalonia;

/// <summary>
/// Converts file-system paths into file:// URIs before passing them to the
/// platform-specific web engine. Absolute URIs (https:, about:, data:, etc.)
/// remain untouched. Relative paths are based on AppContext.BaseDirectory,
/// not on the process working directory.
/// </summary>
internal static class WebViewUrlNormalizer
{
    internal static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "about:blank";

        var input = url.Trim();

        if (Path.IsPathFullyQualified(input))
            return ToFileUri(input);

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
            return uri.IsFile ? uri.AbsoluteUri : input;

        return ToFileUri(Path.GetFullPath(input, AppContext.BaseDirectory));
    }

    private static string ToFileUri(string path) =>
        new Uri(Path.GetFullPath(path)).AbsoluteUri;
}
