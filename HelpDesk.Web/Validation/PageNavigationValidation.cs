namespace HelpDesk.Web.Validation;

/// <summary>
/// Validation helpers for page navigation fields (Route and Url).
/// </summary>
public static class PageNavigationValidation
{
    /// <summary>
    /// Validates that Route and Url are mutually exclusive (both can be empty, but not both filled).
    /// </summary>
    /// <param name="route">The route value.</param>
    /// <param name="url">The url value.</param>
    /// <returns>Error text, or null when valid.</returns>
    public static string? ErrorIfBothRouteAndUrl(string? route, string? url)
    {
        var hasRoute = !string.IsNullOrWhiteSpace(route);
        var hasUrl = !string.IsNullOrWhiteSpace(url);
        
        if (hasRoute && hasUrl)
            return "A page can have either a Route or a URL, but not both.";
            
        return null;
    }
    
    /// <summary>
    /// Validates that OpenInNewTab is only set when Url is provided.
    /// </summary>
    /// <param name="url">The url value.</param>
    /// <param name="openInNewTab">The open in new tab flag.</param>
    /// <returns>Error text, or null when valid.</returns>
    public static string? ErrorIfNewTabWithoutUrl(string? url, bool openInNewTab)
    {
        var hasUrl = !string.IsNullOrWhiteSpace(url);
        
        if (openInNewTab && !hasUrl)
            return "Open in New Tab can only be set when a URL is provided.";
            
        return null;
    }
    
    /// <summary>
    /// Validates URL format when provided.
    /// </summary>
    /// <param name="url">The url value.</param>
    /// <returns>Error text, or null when valid.</returns>
    public static string? ErrorIfInvalidUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
            
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "URL must be a valid absolute URL.";
            
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return "URL must use HTTP or HTTPS protocol.";
            
        return null;
    }
}