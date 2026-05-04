using FoiaProcessor.Api.Contracts;
using FoiaProcessor.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace FoiaProcessor.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    private readonly HealthProbeService _probes;

    public HealthController(HealthProbeService probes)
    {
        _probes = probes;
    }

    /// <summary>Cheap liveness probe (always 200 if the process is up).</summary>
    [HttpGet("")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Liveness()
        => Ok(new { status = "ok", timestamp = DateTimeOffset.UtcNow });

    /// <summary>Detailed system health report covering every external and in-process component.</summary>
    [HttpGet("detailed")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(SystemHealthReport), StatusCodes.Status200OK)]
    public async Task<ActionResult<SystemHealthReport>> Detailed(CancellationToken ct)
    {
        var report = await _probes.RunAsync(ct);
        return Ok(report);
    }
}
