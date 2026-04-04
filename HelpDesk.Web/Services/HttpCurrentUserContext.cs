using System.Security.Claims;
using HelpDesk.Logging;
namespace HelpDesk.Web.Services;

/// <summary>
/// Resolve the current user id from HTTP claims for change history attribution.
/// </summary>
public sealed class HttpCurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _http;

    /// <summary>
    /// Create HTTP-backed user context.
    /// </summary>
    /// <param name="http">HTTP context accessor <see cref="IHttpContextAccessor"/>.</param>
    public HttpCurrentUserContext(IHttpContextAccessor http)
    {
        _http = http;
    }

    public int? UserId
    {
        get
        {
            var id = _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(id, out var v) ? v : null;
        }
    }
}
