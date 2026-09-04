using H1Stats.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace H1Stats.Api.Controllers;

[ApiController]
[Route("api/sandbox")]
public class SandBoxController : ControllerBase
{
    private readonly ICm2DevDatabase _cm2Dev;

    public SandBoxController(ICm2DevDatabase cm2Dev) => _cm2Dev = cm2Dev;

    /// <summary>CM2 DEV connection status for the SandBox tab (never production CM2).</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var test = await _cm2Dev.TestConnectionAsync(ct);
        return Ok(new
        {
            dataset = "CM2DEV",
            isDev = true,
            endpoint = _cm2Dev.EndpointLabel,
            schema = _cm2Dev.Schema,
            connected = test.Connected,
            message = test.Message,
            testedAt = test.TestedAt,
        });
    }

    [HttpGet("workllists/sites")]
    public async Task<IActionResult> GetWorklistSites(CancellationToken ct)
    {
        try
        {
            var sites = await _cm2Dev.GetSitesAsync(ct);
            return Ok(sites.Select(s => new { id = s.Id, displayName = s.DisplayName }));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    /// <summary>Unarrived pink recalls from CM2 DEV only.</summary>
    [HttpGet("workllists/pink-recalls")]
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
            var result = await _cm2Dev.GetUnarrivedPinkRecallsAsync(from, to, site, cardiologist, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    public sealed record UpdateAppointmentNoteRequest(string? Note);

    [HttpPut("workllists/appointments/{appointmentId}/note")]
    public async Task<IActionResult> UpdateAppointmentNote(
        string appointmentId,
        [FromBody] UpdateAppointmentNoteRequest request,
        CancellationToken ct)
    {
        var (ok, error) = await _cm2Dev.UpdateAppointmentNoteAsync(appointmentId, request.Note, ct);
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

    public sealed record UpdateAppointmentServiceTypeRequest(string ServiceTypeId);

    [HttpPut("workllists/appointments/{appointmentId}/service-type")]
    public async Task<IActionResult> UpdateAppointmentServiceType(
        string appointmentId,
        [FromBody] UpdateAppointmentServiceTypeRequest request,
        CancellationToken ct)
    {
        var (ok, error, serviceTypeId, serviceTitle) =
            await _cm2Dev.UpdateAppointmentServiceTypeAsync(appointmentId, request.ServiceTypeId, ct);
        if (!ok)
        {
            if (string.Equals(error, "Appointment not found", StringComparison.OrdinalIgnoreCase)
                || string.Equals(error, "Invalid appointment id", StringComparison.OrdinalIgnoreCase))
                return NotFound(new { message = error });
            if (error is not null && error.Contains("lacks UPDATE", StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, new { message = error });
            return BadRequest(new { message = error ?? "Failed to update service type" });
        }

        return Ok(new { appointmentId, serviceTypeId, serviceTitle });
    }

    [HttpPost("workllists/appointments/{appointmentId}/arrive")]
    public async Task<IActionResult> ArriveAppointment(string appointmentId, CancellationToken ct)
    {
        var (ok, error) = await _cm2Dev.ArriveAppointmentAsync(appointmentId, ct);
        if (!ok)
        {
            if (string.Equals(error, "Appointment not found", StringComparison.OrdinalIgnoreCase)
                || string.Equals(error, "Invalid appointment id", StringComparison.OrdinalIgnoreCase))
                return NotFound(new { message = error });
            if (error is not null && error.Contains("lacks UPDATE", StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, new { message = error });
            return BadRequest(new { message = error ?? "Failed to arrive appointment" });
        }

        return Ok(new { appointmentId, arrived = true });
    }

    public sealed record UpdateAppointmentScheduleRequest(string Date, string? Time);

    [HttpPut("workllists/appointments/{appointmentId}/schedule")]
    public async Task<IActionResult> UpdateAppointmentSchedule(
        string appointmentId,
        [FromBody] UpdateAppointmentScheduleRequest request,
        CancellationToken ct)
    {
        var (ok, error, date, time) =
            await _cm2Dev.UpdateAppointmentScheduleAsync(appointmentId, request.Date, request.Time, ct);
        if (!ok)
        {
            if (string.Equals(error, "Appointment not found", StringComparison.OrdinalIgnoreCase)
                || string.Equals(error, "Invalid appointment id", StringComparison.OrdinalIgnoreCase))
                return NotFound(new { message = error });
            if (error is not null && error.StartsWith("Clash:", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { message = error, clash = true });
            if (error is not null && error.Contains("lacks UPDATE", StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, new { message = error });
            return BadRequest(new { message = error ?? "Failed to update schedule" });
        }

        return Ok(new
        {
            appointmentId,
            appointmentDate = date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            appointmentTime = time,
        });
    }

    [HttpGet("workllists/appointments/{appointmentId}/available-slots")]
    public async Task<IActionResult> GetAvailableSlots(
        string appointmentId,
        [FromQuery] string date,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(date))
            return BadRequest(new { message = "date is required (yyyy-MM-dd)" });

        var result = await _cm2Dev.GetAvailableDiarySlotsAsync(appointmentId, date, ct);
        if (string.Equals(result.Message, "Appointment not found", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result.Message, "Invalid appointment id", StringComparison.OrdinalIgnoreCase))
            return NotFound(new { message = result.Message });
        if (string.Equals(result.Message, "Invalid date", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = result.Message });

        return Ok(result);
    }

    /// <summary>
    /// Patients with Initial/Follow-up consults in the last N months and no H1 cardiologist (one row per patient).
    /// </summary>
    [HttpGet("unassigned-cardiologists")]
    public async Task<IActionResult> GetUnassignedCardiologists(
        [FromQuery] int lookbackMonths = 12,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _cm2Dev.GetUnassignedConsultPatientsAsync(lookbackMonths, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    public sealed record AssignCardiologistRequest(string CardiologistId);

    /// <summary>Assigns PATIENT.PAT_CARDIOLOGIST on CM2 DEV (removes patient from unassigned list).</summary>
    [HttpPut("unassigned-cardiologists/{patientRid}/assign")]
    public async Task<IActionResult> AssignCardiologist(
        string patientRid,
        [FromBody] AssignCardiologistRequest request,
        CancellationToken ct)
    {
        var (ok, error, cardiologistId, cardiologistName) =
            await _cm2Dev.AssignPatientCardiologistAsync(patientRid, request.CardiologistId, ct);
        if (!ok)
        {
            if (string.Equals(error, "Patient not found", StringComparison.OrdinalIgnoreCase)
                || string.Equals(error, "Invalid patient id", StringComparison.OrdinalIgnoreCase))
                return NotFound(new { message = error });
            if (error is not null && error.Contains("lacks UPDATE", StringComparison.OrdinalIgnoreCase))
                return StatusCode(403, new { message = error });
            return BadRequest(new { message = error ?? "Failed to assign cardiologist" });
        }

        return Ok(new { patientRid, cardiologistId, cardiologistName });
    }
}
