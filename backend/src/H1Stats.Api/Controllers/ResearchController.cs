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
    private readonly IResearchScreeningJobService _screeningJobs;
    private readonly IResearchSlackClient _slack;
    private readonly ICm2Repository _cm2;

    public ResearchController(
        IResearchLlmClient llm,
        IResearchStudyStore studies,
        IResearchScreeningService screening,
        IResearchScreeningResultStore screeningResults,
        IResearchScreeningJobService screeningJobs,
        IResearchSlackClient slack,
        ICm2Repository cm2)
    {
        _llm = llm;
        _studies = studies;
        _screening = screening;
        _screeningResults = screeningResults;
        _screeningJobs = screeningJobs;
        _slack = slack;
        _cm2 = cm2;
    }

    [HttpGet("llm/status")]
    public async Task<IActionResult> GetLlmStatus(CancellationToken ct) =>
        Ok(await _llm.GetStatusAsync(ct));

    [HttpGet("slack/status")]
    public IActionResult GetSlackStatus() => Ok(_slack.GetStatus());

    [HttpPost("slack/candidate-report")]
    public async Task<IActionResult> PostCandidateReport(
        [FromBody] PostResearchSlackReportRequest? request,
        CancellationToken ct)
    {
        var text = request?.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(text))
            return BadRequest(new { message = "Report text is required." });
        if (text.Length > 40_000)
            return BadRequest(new { message = "Report text is too long for Slack." });

        var (ok, error) = await _slack.PostCandidateReportAsync(text, ct);
        if (!ok)
            return BadRequest(new { message = error ?? "Could not post to Slack." });
        return Ok(new { ok = true });
    }

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
                    DocumentsReviewed = screening.DocumentsReviewed,
                    PrimaryLettersRid = screening.PrimaryLettersRid,
                    PrimaryLetterPath = screening.PrimaryLetterPath,
                    PrimaryLetterDate = screening.PrimaryLetterDate,
                    SourceFileFingerprint = screening.SourceFileFingerprint,
                    PatientRid = screening.PatientRid ?? c.PatientRid,
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

    [HttpPatch("upcoming-consults/{appointmentId}/screening/suitability")]
    public async Task<IActionResult> OverrideSuitability(
        string appointmentId,
        [FromBody] OverrideResearchSuitabilityRequest? request,
        CancellationToken ct)
    {
        var overall = request?.Overall?.Trim().ToUpperInvariant();
        if (overall is not (ResearchSuitability.LikelySuitable or ResearchSuitability.LikelyNotSuitable))
            return BadRequest(new { message = "Overall must be LIKELY_SUITABLE or LIKELY_NOT_SUITABLE." });
        if (string.IsNullOrWhiteSpace(request?.StudyId))
            return BadRequest(new { message = "Study id is required." });

        var existing = await _screeningResults.GetAsync(appointmentId, ct);
        if (existing is null)
            return NotFound(new { message = "No screening result saved for this appointment." });

        var index = existing.Studies.ToList().FindIndex(s =>
            string.Equals(s.StudyId, request.StudyId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return NotFound(new { message = "That study is not in this screening result." });

        var studies = existing.Studies.ToList();
        var current = studies[index];
        studies[index] = current with { Overall = overall, Error = null };
        var saved = existing with { Studies = studies };
        await _screeningResults.UpsertAsync(saved, ct);
        return Ok(await _screeningResults.GetAsync(appointmentId, ct));
    }

    [HttpGet("screening-jobs/current")]
    public IActionResult GetCurrentScreeningJob() =>
        Ok(_screeningJobs.GetCurrent());

    [HttpPost("screening-jobs")]
    public IActionResult StartScreeningJob([FromBody] StartResearchScreeningJobRequest? request)
    {
        var (job, error, conflict) = _screeningJobs.Start(request ?? new StartResearchScreeningJobRequest([]));
        if (conflict)
            return Conflict(new { message = error, job });
        if (error is not null)
            return BadRequest(new { message = error });
        return StatusCode(StatusCodes.Status202Accepted, job);
    }

    [HttpPost("screening-jobs/current/cancel")]
    public IActionResult CancelScreeningJob() =>
        Ok(_screeningJobs.Cancel());

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
}
