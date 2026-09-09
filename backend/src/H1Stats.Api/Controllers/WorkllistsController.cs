using H1Stats.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace H1Stats.Api.Controllers;

/// <summary>
/// Production CM2 worklists — read-only. Schedule mutations belong on SandBox (CM2DEV).
/// </summary>
[ApiController]
[Route("api/workllists")]
public class WorkllistsController : ControllerBase
{
    private readonly ICm2Repository _cm2;

    public WorkllistsController(ICm2Repository cm2)
    {
        _cm2 = cm2;
    }

    /// <summary>
    /// Unarrived pink (Cardiology Recall) diary bookings.
    /// Omit dateFrom/dateTo for all unarrived; optional site and cardiologist filters.
    /// </summary>
    [HttpGet("pink-recalls")]
    public async Task<IActionResult> GetUnarrivedPinkRecalls(
        [FromQuery] string? dateFrom,
        [FromQuery] string? dateTo,
        [FromQuery] string? site,
        [FromQuery] string? cardiologist,
        CancellationToken ct)
    {
        var hasFrom = !string.IsNullOrWhiteSpace(dateFrom);
        var hasTo = !string.IsNullOrWhiteSpace(dateTo);

        DateOnly? from = null;
        DateOnly? to = null;

        if (hasFrom || hasTo)
        {
            if (!hasFrom || !DateOnly.TryParse(dateFrom, out var parsedFrom))
                return BadRequest(new { message = "Invalid or missing dateFrom (leave both dates blank for all unarrived)" });
            if (!hasTo || !DateOnly.TryParse(dateTo, out var parsedTo))
                return BadRequest(new { message = "Invalid or missing dateTo (leave both dates blank for all unarrived)" });
            if (parsedFrom > parsedTo)
                return BadRequest(new { message = "dateFrom must be on or before dateTo" });
            from = parsedFrom;
            to = parsedTo;
        }

        try
        {
            var result = await _cm2.GetUnarrivedPinkRecallsAsync(from, to, site, cardiologist, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    /// <summary>
    /// Consult-referral documents imported onto a patient with no unarrived Cardiology/Admin recall.
    /// Omit dates for today only (DOC_DATE is date-only). Optional cardiologist filter.
    /// </summary>
    [HttpGet("unactioned-consult-referrals")]
    public async Task<IActionResult> GetUnactionedConsultReferrals(
        [FromQuery] string? dateFrom,
        [FromQuery] string? dateTo,
        [FromQuery] string? cardiologist,
        CancellationToken ct)
    {
        var parsed = ParseDocumentDateRange(dateFrom, dateTo);
        if (parsed.Error is not null)
            return BadRequest(new { message = parsed.Error });

        try
        {
            var result = await _cm2.GetUnactionedConsultReferralsAsync(
                parsed.From, parsed.To, cardiologist, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    /// <summary>Streams the consult-referral PDF to the browser from the CM2 mapped drive.</summary>
    [HttpGet("unactioned-consult-referrals/{documentId}/file")]
    public async Task<IActionResult> GetUnactionedConsultReferralFile(
        string documentId, CancellationToken ct)
    {
        var (file, error) = await _cm2.GetUnactionedConsultReferralFileAsync(documentId, ct);
        if (file is null)
        {
            if (string.Equals(error, "Document not found", StringComparison.OrdinalIgnoreCase)
                || string.Equals(error, "Invalid document id", StringComparison.OrdinalIgnoreCase)
                || (error is not null && error.Contains("not found", StringComparison.OrdinalIgnoreCase)))
                return NotFound(new { message = error });
            return BadRequest(new { message = error ?? "Failed to open referral PDF" });
        }

        Response.Headers.ContentDisposition = $"inline; filename=\"{file.FileName.Replace("\"", "")}\"";
        return PhysicalFile(file.FullPath, file.ContentType);
    }

    /// <summary>
    /// Past CTCA diary bookings with no imported test/results CTCA document. One row per patient.
    /// Defaults to the last 30 past days. Future dates are clamped to yesterday.
    /// </summary>
    [HttpGet("missing-ctca-docs")]
    public async Task<IActionResult> GetMissingCtcaDocs(
        [FromQuery] string? dateFrom,
        [FromQuery] string? dateTo,
        [FromQuery] string? cardiologist,
        CancellationToken ct)
    {
        var parsed = ParsePastDateRange(dateFrom, dateTo);
        if (parsed.Error is not null)
            return BadRequest(new { message = parsed.Error });

        try
        {
            var result = await _cm2.GetMissingCtcaDocsAsync(
                parsed.From, parsed.To, cardiologist, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    private static (DateOnly From, DateOnly To, string? Error) ParseDocumentDateRange(
        string? dateFrom, string? dateTo)
    {
        var hasFrom = !string.IsNullOrWhiteSpace(dateFrom);
        var hasTo = !string.IsNullOrWhiteSpace(dateTo);
        if (!hasFrom && !hasTo)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            return (today, today, null);
        }

        if (!hasFrom || !DateOnly.TryParse(dateFrom, out var parsedFrom))
            return (default, default, "Invalid or missing dateFrom (leave both dates blank for today)");
        if (!hasTo || !DateOnly.TryParse(dateTo, out var parsedTo))
            return (default, default, "Invalid or missing dateTo (leave both dates blank for today)");
        if (parsedFrom > parsedTo)
            return (default, default, "dateFrom must be on or before dateTo");
        return (parsedFrom, parsedTo, null);
    }

    private static (DateOnly From, DateOnly To, string? Error) ParsePastDateRange(
        string? dateFrom, string? dateTo)
    {
        var yesterday = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
        var hasFrom = !string.IsNullOrWhiteSpace(dateFrom);
        var hasTo = !string.IsNullOrWhiteSpace(dateTo);
        if (!hasFrom && !hasTo)
            return (yesterday.AddDays(-29), yesterday, null);

        if (!hasFrom || !DateOnly.TryParse(dateFrom, out var parsedFrom))
            return (default, default, "Invalid or missing dateFrom (leave both dates blank for the last 30 past days)");
        if (!hasTo || !DateOnly.TryParse(dateTo, out var parsedTo))
            return (default, default, "Invalid or missing dateTo (leave both dates blank for the last 30 past days)");
        if (parsedFrom > parsedTo)
            return (default, default, "dateFrom must be on or before dateTo");
        return (parsedFrom, parsedTo, null);
    }
}
