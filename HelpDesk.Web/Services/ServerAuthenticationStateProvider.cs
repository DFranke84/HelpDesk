using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace HelpDesk.Web.Services;

/// <summary>
/// Blazor server authentication state backed by the current HTTP user principal.
/// Caches the principal when HTTP context is available so interactive renders (where context is often null) stay consistent.
/// </summary>
public sealed class ServerAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly IHttpContextAccessor _http;
    private ClaimsPrincipal? _cachedUser;

    /// <summary>
    /// Create server authentication state provider.
    /// </summary>
    /// <param name="http">HTTP context accessor <see cref="IHttpContextAccessor"/>.</param>
    public ServerAuthenticationStateProvider(IHttpContextAccessor http)
    {
        _http = http;
    }

    /// <inheritdoc />
    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var ctx = _http.HttpContext;
        if (ctx is not null)
        {
            var user = ctx.User;
            if (user.Identity?.IsAuthenticated == true)
                _cachedUser = user;
            else
                _cachedUser = null;

            return Task.FromResult(new AuthenticationState(user));
        }

        if (_cachedUser is not null)
            return Task.FromResult(new AuthenticationState(_cachedUser));

        return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    /// <summary>
    /// Notify Blazor that authentication state may have changed.
    /// </summary>
    public void NotifyAuthenticationChanged()
    {
        var ctx = _http.HttpContext;
        if (ctx is not null)
        {
            var user = ctx.User;
            if (user.Identity?.IsAuthenticated == true)
                _cachedUser = user;
            else
                _cachedUser = null;

            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
            return;
        }

        if (_cachedUser is not null)
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_cachedUser)));
        else
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
    }
}
