using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace H1Stats.Api.Controllers;

[ApiController]
[Route("api")]
public class HealthController : ControllerBase
{
    private static readonly DateTime StartedAt = DateTime.UtcNow;
    private readonly IDatabaseConnectionService _db;

    public HealthController(IDatabaseConnectionService db) => _db = db;

    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        var sql = await _db.TestConnectionAsync(ct: ct);
        var uptime = DateTime.UtcNow - StartedAt;
        var response = new HealthResponse(
            "Healthy",
            "1.0.0",
            sql,
            $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m"
        );
        return Ok(response);
    }
}
