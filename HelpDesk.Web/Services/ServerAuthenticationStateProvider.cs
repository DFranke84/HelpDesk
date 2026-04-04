using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace HelpDesk.Web.Services;

/// <summary>
/// Blazor server authentication state backed by the current HTTP user principal.
/// </summary>
public sealed class ServerAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _http;

    /// <summary>
    /// Create server authentication state provider.
    /// </summary>
    /// <param name="http">HTTP context accessor <see cref="IHttpContextAccessor"/>.</param>
    public ServerAuthenticationStateProvider(IHttpContextAccessor http)
    {
        _http = http;
    }

    /// <summary>
    /// Get authentication state from the current request user.
    /// </summary>
    /// <returns>Task yielding the current <see cref="AuthenticationState"/>.</returns>
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = _http.HttpContext?.User
                   ?? new ClaimsPrincipal(new ClaimsIdentity());
        return Task.FromResult(new AuthenticationState(user));
    }

    /// <summary>
    /// Notify Blazor that authentication state may have changed.
    /// </summary>
    /// <returns>This method does not return a value.</returns>
    public void NotifyAuthenticationChanged()
    {
        var user = _http.HttpContext?.User
                   ?? new ClaimsPrincipal(new ClaimsIdentity());
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
    }
}
