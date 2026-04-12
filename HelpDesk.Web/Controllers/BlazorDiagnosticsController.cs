using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HelpDesk.Web.Controllers;

/// <summary>
/// Development-only: receives browser-side diagnostics when the Blazor error UI appears or JS throws.
/// </summary>
[ApiController]
[Route("api/diag")]
public sealed class BlazorDiagnosticsController : ControllerBase
{
    private readonly ILogger<BlazorDiagnosticsController> _log;
    private readonly IHostEnvironment _env;

    public BlazorDiagnosticsController(ILogger<BlazorDiagnosticsController> log, IHostEnvironment env)
    {
        _log = log;
        _env = env;
    }

    /// <summary>Log a client-side event (Development only).</summary>
    [HttpPost("blazor-client")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> PostBlazorClient(CancellationToken cancellationToken)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        // Log at Error severity so NLog's logfile rule (minLevel Error) always persists this line to helpdesk.log.
        _log.LogError("Blazor client diagnostic (browser): {Json}", string.IsNullOrEmpty(json) ? "(empty)" : json);
        return Ok();
    }
}
