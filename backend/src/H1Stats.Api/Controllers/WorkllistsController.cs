using H1Stats.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace H1Stats.Api.Controllers;

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

    public sealed record UpdateAppointmentNoteRequest(string? Note);

    /// <summary>Persists APPOINTMENT_NOTE back to the CM2 diary booking.</summary>
    [HttpPut("appointments/{appointmentId}/note")]
    public async Task<IActionResult> UpdateAppointmentNote(
        string appointmentId,
        [FromBody] UpdateAppointmentNoteRequest request,
        CancellationToken ct)
    {
        var (ok, error) = await _cm2.UpdateAppointmentNoteAsync(appointmentId, request.Note, ct);
        if (!ok)
        {
            if (string.Equals(error, "Appointment not found", StringComparison.OrdinalIgnoreCase)
                || string.Equals(error, "Invalid appointment id", StringComparison.OrdinalIgnoreCase))
                return NotFound(new { message = error });
            if (error is not null && error.Contains("lacks UPDATE", StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, new { message = error });
            return BadRequest(new { message = error ?? "Failed to update note" });
        }

        return Ok(new { appointmentId, note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim() });
    }
}
