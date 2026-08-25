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
        [FromQuery] string? site,
        CancellationToken ct)
    {
        if (!TryParseDateRange(dateFrom, dateTo, out var from, out var to, out var error))
            return BadRequest(new { message = error });

        try
        {
            var topN = Cm2TopN.Clamp(top);
            var report = await _cm2.GetTopReferringDoctorsAsync(from, to, topN, investigationType, site, ct);
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

    [HttpGet("cm2/sites")]
    public async Task<IActionResult> GetCm2Sites(CancellationToken ct)
    {
        try
        {
            var sites = await _cm2.GetSitesAsync(ct);
            return Ok(sites.Select(s => new { id = s.Id, displayName = s.DisplayName }));
        }
        catch
        {
            var list = Cm2SiteMapping.All
                .Select(x => new { id = x.Code, displayName = x.DisplayName });
            return Ok(list);
        }
    }

    [HttpGet("cm2/top-referring-practices")]
    public async Task<IActionResult> GetTopReferringPractices(
        [FromQuery] string? dateFrom,
        [FromQuery] string? dateTo,
        [FromQuery] int? top,
        [FromQuery] string? site,
        CancellationToken ct)
    {
        if (!TryParseDateRange(dateFrom, dateTo, out var from, out var to, out var error))
            return BadRequest(new { message = error });

        try
        {
            var topN = Cm2TopN.Clamp(top);
            var report = await _cm2.GetTopReferringPracticesAsync(from, to, topN, site, ct);
            return Ok(report);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("cm2/referrers-growth")]
    public async Task<IActionResult> GetReferrersGrowth(
        [FromQuery] int? year,
        [FromQuery] int? quarter,
        [FromQuery] int? top,
        [FromQuery] string? investigationType,
        [FromQuery] string? sortBy,
        [FromQuery] string? mode,
        [FromQuery] string? site,
        CancellationToken ct)
    {
        if (!QuarterPeriod.TryParse(year, quarter, out var current, out var error))
            return BadRequest(new { message = error });

        try
        {
            var topN = Cm2TopN.Clamp(top);
            var report = await _cm2.GetReferrersGrowthByQuarterAsync(
                current.Year, current.Quarter, topN, investigationType, sortBy, mode, site, ct);
            return Ok(report);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("cm2/default-quarter")]
    public IActionResult GetDefaultQuarter()
    {
        var q = QuarterPeriod.LastCompleted();
        return Ok(new { year = q.Year, quarter = q.Quarter, label = q.Label });
    }

    [HttpGet("cm2/referring-doctors/search")]
    public async Task<IActionResult> SearchReferringDoctors([FromQuery] string? q, CancellationToken ct)
    {
        try
        {
            return Ok(await _cm2.SearchReferringDoctorsAsync(q ?? string.Empty, ct));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("cm2/referring-practices/search")]
    public async Task<IActionResult> SearchReferringPractices([FromQuery] string? q, CancellationToken ct)
    {
        try
        {
            return Ok(await _cm2.SearchReferringPracticesAsync(q ?? string.Empty, ct));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("cm2/practice-doctors")]
    public async Task<IActionResult> GetPracticeDoctors(
        [FromQuery] string? id,
        [FromQuery] string? investigationType,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { message = "id is required" });

        try
        {
            return Ok(await _cm2.GetPracticeDoctorsAsync(id, investigationType, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("cm2/referrer-history/studies")]
    public async Task<IActionResult> GetReferrerHistoryStudies(
        [FromQuery] string? mode,
        [FromQuery] string? id,
        [FromQuery] string? investigationType,
        [FromQuery] string? period,
        [FromQuery] string? dateFrom,
        [FromQuery] string? dateTo,
        [FromQuery] string? grain,
        [FromQuery] string? doctorId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return BadRequest(new { message = "mode is required (doctor or practice)" });
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { message = "id is required" });
        if (string.IsNullOrWhiteSpace(investigationType))
            return BadRequest(new { message = "investigationType is required" });
        if (string.IsNullOrWhiteSpace(period))
            return BadRequest(new { message = "period is required" });
        if (!TryParseDateRange(dateFrom, dateTo, out var from, out var to, out var error))
            return BadRequest(new { message = error });

        try
        {
            var result = await _cm2.GetReferrerHistoryStudiesAsync(
                mode, id, investigationType, period, from, to, grain, doctorId, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("cm2/referrer-history")]
    public async Task<IActionResult> GetReferrerHistory(
        [FromQuery] string? mode,
        [FromQuery] string? id,
        [FromQuery] string? investigationType,
        [FromQuery] string? dateFrom,
        [FromQuery] string? dateTo,
        [FromQuery] string? grain,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return BadRequest(new { message = "mode is required (doctor or practice)" });
        if (string.IsNullOrWhiteSpace(id))
            return BadRequest(new { message = "id is required" });
        if (!TryParseDateRange(dateFrom, dateTo, out var from, out var to, out var error))
            return BadRequest(new { message = error });

        try
        {
            var report = await _cm2.GetReferrerHistoryAsync(mode, id, investigationType, from, to, grain, ct);
            return Ok(report);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("cm2/studies/{testRid}/report")]
    public async Task<IActionResult> GetStudyReport(string testRid, CancellationToken ct)
    {
        try
        {
            var file = await _cm2.GetStudyReportFileAsync(testRid, ct);
            if (file is null)
                return NotFound(new { message = "No report on file for this study" });
            Response.Headers.ContentDisposition = $"inline; filename=\"{file.FileName}\"";
            return PhysicalFile(file.FullPath, file.ContentType);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("cm2/studies/{testRid}/open-report")]
    public async Task<IActionResult> OpenStudyReport(string testRid, CancellationToken ct)
    {
        try
        {
            await _cm2.OpenStudyReportAsync(testRid, ct);
            return Ok(new { opened = true });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (FileNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("cm2/dr-summary")]
    public async Task<IActionResult> GetDrSummary(
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken ct)
    {
        if (!MonthPeriod.TryParse(year, month, out var period, out var error))
            return BadRequest(new { message = error });

        try
        {
            var report = await _cm2.GetDrSummaryAsync(period.Year, period.Month, ct);
            return Ok(report);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("cm2/dr-summary/history")]
    public async Task<IActionResult> GetDrSummaryHistory(
        [FromQuery] string? physician,
        [FromQuery] int? year,
        [FromQuery] int? month,
        CancellationToken ct)
    {
        if (!MonthPeriod.TryParse(year, month, out var period, out var error))
            return BadRequest(new { message = error });

        try
        {
            var report = await _cm2.GetDrSummaryHistoryAsync(physician, period.Year, period.Month, ct);
            return Ok(report);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
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
