using H1Stats.Core.Interfaces;
using H1Stats.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace H1Stats.Api.Controllers;

[ApiController]
[Route("api/clinical")]
public class ClinicalController : ControllerBase
{
    private readonly IPhysicianStatisticsRepository _syngo;
    private readonly ICm2Repository _cm2;

    public ClinicalController(IPhysicianStatisticsRepository syngo, ICm2Repository cm2)
    {
        _syngo = syngo;
        _cm2 = cm2;
    }

    [HttpGet("physicians")]
    public async Task<IActionResult> GetPhysicians(CancellationToken ct)
    {
        try
        {
            return Ok(await _syngo.GetPhysiciansAsync(ct));
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
        if (!TryParseDateRange(dateFrom, dateTo, out var from, out var to, out var error))
            return BadRequest(new { message = error });

        var report = await _syngo.GetReportAsync(from, to, physician, ct);
        return Ok(report);
    }

    [HttpGet("cm2/physicians")]
    public async Task<IActionResult> GetCm2Physicians(CancellationToken ct)
    {
        try
        {
            return Ok(await _cm2.GetCardiologistsAsync(ct));
        }
        catch
        {
            return Ok(Array.Empty<string>());
        }
    }

    [HttpGet("cm2/physician-statistics")]
    public async Task<IActionResult> GetCm2PhysicianStatistics(
        [FromQuery] string? dateFrom,
        [FromQuery] string? dateTo,
        [FromQuery] string? physician,
        CancellationToken ct)
    {
        if (!TryParseDateRange(dateFrom, dateTo, out var from, out var to, out var error))
            return BadRequest(new { message = error });

        try
        {
            var report = await _cm2.GetReportingDrReportAsync(from, to, physician, ct);
            return Ok(report);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("cm2/top-referring-doctors")]
    public async Task<IActionResult> GetTopReferringDoctors(
        [FromQuery] string? dateFrom,
        [FromQuery] string? dateTo,
        [FromQuery] int? top,
        [FromQuery] string? investigationType,
        CancellationToken ct)
    {
        if (!TryParseDateRange(dateFrom, dateTo, out var from, out var to, out var error))
            return BadRequest(new { message = error });

        try
        {
            var topN = Cm2TopN.Clamp(top);
            var report = await _cm2.GetTopReferringDoctorsAsync(from, to, topN, investigationType, ct);
            return Ok(report);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("cm2/investigation-types")]
    public IActionResult GetCm2InvestigationTypes()
    {
        var list = Cm2InvestigationTypeMapping.ForReferringDoctors
            .Select(x => new { id = x.Code, displayName = x.DisplayName });
        return Ok(list);
    }

    [HttpGet("cm2/top-referring-practices")]
    public async Task<IActionResult> GetTopReferringPractices(
        [FromQuery] string? dateFrom,
        [FromQuery] string? dateTo,
        [FromQuery] int? top,
        CancellationToken ct)
    {
        if (!TryParseDateRange(dateFrom, dateTo, out var from, out var to, out var error))
            return BadRequest(new { message = error });

        try
        {
            var topN = Cm2TopN.Clamp(top);
            var report = await _cm2.GetTopReferringPracticesAsync(from, to, topN, ct);
            return Ok(report);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    private static bool TryParseDateRange(
        string? dateFrom, string? dateTo, out DateOnly from, out DateOnly to, out string? error)
    {
        from = default;
        to = default;
        error = null;

        if (string.IsNullOrWhiteSpace(dateFrom) || !DateOnly.TryParse(dateFrom, out from))
        {
            error = "Invalid or missing dateFrom";
            return false;
        }

        if (string.IsNullOrWhiteSpace(dateTo))
            to = DateOnly.FromDateTime(DateTime.Today);
        else if (!DateOnly.TryParse(dateTo, out to))
        {
            error = "Invalid dateTo";
            return false;
        }

        if (from > to)
        {
            error = "dateFrom must be on or before dateTo";
            return false;
        }

        return true;
    }
}
