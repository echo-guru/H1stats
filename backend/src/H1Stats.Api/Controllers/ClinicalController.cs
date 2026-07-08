using H1Stats.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace H1Stats.Api.Controllers;

[ApiController]
[Route("api/clinical")]
public class ClinicalController : ControllerBase
{
    private readonly IPhysicianStatisticsRepository _repository;

    public ClinicalController(IPhysicianStatisticsRepository repository) => _repository = repository;

    [HttpGet("physicians")]
    public async Task<IActionResult> GetPhysicians(CancellationToken ct)
    {
        try
        {
            return Ok(await _repository.GetPhysiciansAsync(ct));
        }
        catch
        {
            return Ok(Array.Empty<string>());
        }
    }

    [HttpGet("physician-statistics")]
    public async Task<IActionResult> GetPhysicianStatistics(
        [FromQuery] string? dateFrom,
        [FromQuery] string? dateTo,
        [FromQuery] string? physician,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dateFrom) || !DateOnly.TryParse(dateFrom, out var from))
            return BadRequest(new { message = "Invalid or missing dateFrom" });

        DateOnly to;
        if (string.IsNullOrWhiteSpace(dateTo))
            to = DateOnly.FromDateTime(DateTime.Today);
        else if (!DateOnly.TryParse(dateTo, out to))
            return BadRequest(new { message = "Invalid dateTo" });

        if (from > to)
            return BadRequest(new { message = "dateFrom must be on or before dateTo" });

        var report = await _repository.GetReportAsync(from, to, physician, ct);
        return Ok(report);
    }
}
