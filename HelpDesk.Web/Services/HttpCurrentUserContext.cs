using System.Security.Claims;
using HelpDesk.Logging;
namespace HelpDesk.Web.Services;

public sealed class HttpCurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _http;

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
