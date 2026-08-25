using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace H1Stats.Api.Controllers;

[ApiController]
[Route("api/research")]
public class ResearchController : ControllerBase
{
    private readonly IResearchLlmClient _llm;
    private readonly IResearchStudyStore _studies;
    private readonly IResearchScreeningService _screening;
    private readonly IResearchScreeningResultStore _screeningResults;
    private readonly ICm2Repository _cm2;

    public ResearchController(
        IResearchLlmClient llm,
        IResearchStudyStore studies,
        IResearchScreeningService screening,
        IResearchScreeningResultStore screeningResults,
        ICm2Repository cm2)
    {
        _llm = llm;
        _studies = studies;
        _screening = screening;
        _screeningResults = screeningResults;
        _cm2 = cm2;
    }

    [HttpGet("llm/status")]
    public async Task<IActionResult> GetLlmStatus(CancellationToken ct) =>
        Ok(await _llm.GetStatusAsync(ct));

    [HttpGet("upcoming-consults")]
    public async Task<IActionResult> GetUpcomingConsults(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] string? cardiologist,
        CancellationToken ct)
    {
        if (!TryParseDate(from, out var dateFrom) || !TryParseDate(to, out var dateTo))
            return BadRequest(new { message = "Query parameters 'from' and 'to' are required (YYYY-MM-DD)." });

        var span = dateTo.DayNumber - dateFrom.DayNumber;
        if (span < 0)
            return BadRequest(new { message = "'to' must be on or after 'from'." });
        if (span > 92)
            return BadRequest(new { message = "Date range cannot exceed 93 days." });

        try
        {
            var result = await _cm2.GetUpcomingConsultsAsync(dateFrom, dateTo, cardiologist, ct);
            var saved = await _screeningResults.GetManyAsync(
                result.Consults.Select(c => c.AppointmentId), ct);
            var enriched = result.Consults.Select(c =>
            {
                if (!saved.TryGetValue(c.AppointmentId, out var screening))
                    return c;
                return c with
                {
                    Status = screening.Status,
                    ScreenedAt = screening.ScreenedAt,
                    Studies = screening.Studies,
                };
            }).ToList();

            return Ok(result with { Consults = enriched, Count = enriched.Count });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { message = $"Could not load upcoming consults from CM2: {ex.Message}" });
        }
    }

    [HttpGet("upcoming-consults/{appointmentId}/screening")]
    public async Task<IActionResult> GetAppointmentScreening(string appointmentId, CancellationToken ct)
    {
        var result = await _screeningResults.GetAsync(appointmentId, ct);
        return result is null
            ? NotFound(new { message = "No screening result saved for this appointment." })
            : Ok(result);
    }

    [HttpPost("upcoming-consults/{appointmentId}/screen")]
    public async Task<IActionResult> ScreenAppointment(
        string appointmentId,
        [FromBody] ScreenAppointmentRequest? request,
        CancellationToken ct)
    {
        try
        {
            var result = await _screening.ScreenAppointmentAsync(appointmentId, request, ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(504, new { message = "Screening timed out waiting for the local LLM." });
        }
        catch (Exception ex)
        {
            return StatusCode(503, new { message = $"Screening failed: {ex.Message}" });
        }
    }

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParse(value, out date);

    [HttpGet("studies")]
    public async Task<IActionResult> ListStudies(CancellationToken ct) =>
        Ok(await _studies.ListAsync(ct));

    [HttpGet("studies/{id}")]
    public async Task<IActionResult> GetStudy(string id, CancellationToken ct)
    {
        var study = await _studies.GetAsync(id, ct);
        return study is null ? NotFound(new { message = "Study not found" }) : Ok(study);
    }

    [HttpPost("studies")]
    public async Task<IActionResult> CreateStudy([FromBody] SaveResearchStudyRequest request, CancellationToken ct)
    {
        var (study, error) = await _studies.CreateAsync(request, ct);
        if (error is not null)
            return BadRequest(new { message = error });
        return Ok(study);
    }

    [HttpPut("studies/{id}")]
    public async Task<IActionResult> UpdateStudy(
        string id, [FromBody] SaveResearchStudyRequest request, CancellationToken ct)
    {
        var (study, error) = await _studies.UpdateAsync(id, request, ct);
        if (error == "Study not found")
            return NotFound(new { message = error });
        if (error is not null)
            return BadRequest(new { message = error });
        return Ok(study);
    }

    [HttpDelete("studies/{id}")]
    public async Task<IActionResult> DeleteStudy(string id, CancellationToken ct)
    {
        var (deleted, error) = await _studies.DeleteAsync(id, ct);
        if (error == "Study not found")
            return NotFound(new { message = error });
        if (error is not null)
            return BadRequest(new { message = error });
        return deleted ? Ok() : NotFound();
    }

    [HttpGet("synthetic-patient")]
    public IActionResult GetSyntheticPatient() => Ok(_screening.GetSyntheticPatient());

    [HttpGet("dummy-rtf")]
    public IActionResult GetDummyRtf() => Ok(_screening.GetDummyRtfPreview());

    [HttpPost("screen-synthetic")]
    public Task<IActionResult> ScreenSynthetic(CancellationToken ct) =>
        RunScreening(() => _screening.ScreenSyntheticPatientAsync(ct), ct);

    [HttpPost("screen-dummy-rtf")]
    public Task<IActionResult> ScreenDummyRtf(CancellationToken ct) =>
        RunScreening(() => _screening.ScreenDummyRtfAsync(ct), ct);

    private async Task<IActionResult> RunScreening(
        Func<Task<SyntheticScreeningResult>> run, CancellationToken ct)
    {
        try
        {
            var result = await run();
            if (result.Error is not null && result.Studies.Count == 0)
                return BadRequest(new { message = result.Error });
            return Ok(result);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(504, new { message = "Screening timed out waiting for the local LLM." });
        }
    }
}
