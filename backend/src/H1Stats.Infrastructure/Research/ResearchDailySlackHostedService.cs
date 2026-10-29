using H1Stats.Core.Configuration;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using H1Stats.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace H1Stats.Infrastructure.Research;

/// <summary>
/// Weekday morning job: screen today's CM2 consults, then post the candidate report to Slack.
/// </summary>
public sealed class ResearchDailySlackHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan JobWaitTimeout = TimeSpan.FromHours(8);
    private static readonly TimeSpan IdlePoll = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopes;
    private readonly IResearchScreeningJobService _jobs;
    private readonly IResearchScreeningResultStore _results;
    private readonly IResearchDailyAutomationStore _automation;
    private readonly IOptionsMonitor<ResearchDailySlackSettings> _schedule;
    private readonly IOptionsMonitor<ResearchSlackSettings> _slackSettings;
    private readonly ILogger<ResearchDailySlackHostedService> _log;
    private readonly string _lastRunPath;

    public ResearchDailySlackHostedService(
        IServiceScopeFactory scopes,
        IResearchScreeningJobService jobs,
        IResearchScreeningResultStore results,
        IResearchDailyAutomationStore automation,
        IOptionsMonitor<ResearchDailySlackSettings> schedule,
        IOptionsMonitor<ResearchSlackSettings> slackSettings,
        IHostEnvironment env,
        ILogger<ResearchDailySlackHostedService> log)
    {
        _scopes = scopes;
        _jobs = jobs;
        _results = results;
        _automation = automation;
        _schedule = schedule;
        _slackSettings = slackSettings;
        _log = log;
        _lastRunPath = Path.Combine(env.ContentRootPath, "App_Data", "research-daily-slack-last-run.txt");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var startup = _automation.GetStatus();
        _log.LogInformation(
            "Research daily Slack scheduler started (enabled={Enabled}, {Hour:00}:{Minute:00} local, weekdaysOnly={WeekdaysOnly}, source={Source})",
            startup.Enabled,
            startup.Hour,
            startup.Minute,
            startup.WeekdaysOnly,
            startup.Source);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = _schedule.CurrentValue;
                if (!_automation.IsEnabled)
                {
                    await Task.Delay(IdlePoll, stoppingToken);
                    continue;
                }

                var tz = settings.ResolveTimeZone();
                var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
                var today = DateOnly.FromDateTime(nowLocal);
                var scheduled = ClampTime(settings.Hour, settings.Minute);
                var lastRun = LoadLastRunDate();

                var weekdayOk = !settings.WeekdaysOnly
                    || (today.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday);
                var pastSchedule = nowLocal.TimeOfDay >= scheduled.ToTimeSpan();
                var due = weekdayOk && pastSchedule && lastRun != today;

                if (due)
                {
                    await RunForDayAsync(today, stoppingToken);
                    SaveLastRunDate(today);
                }

                var delay = NextWakeDelay(settings, tz, DateTime.UtcNow);
                if (delay < TimeSpan.FromSeconds(5))
                    delay = TimeSpan.FromSeconds(5);
                if (delay > TimeSpan.FromHours(6))
                    delay = TimeSpan.FromHours(6);
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Research daily Slack scheduler loop failed");
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task RunForDayAsync(DateOnly day, CancellationToken ct)
    {
        _log.LogInformation("Research daily Slack run starting for {Day}", day);

        if (!_slackSettings.CurrentValue.IsConfigured)
        {
            _log.LogWarning("Skipping daily Slack post — ResearchSlack:WebhookUrl is not configured");
        }

        IReadOnlyList<ResearchUpcomingConsult> consults;
        await using (var scope = _scopes.CreateAsyncScope())
        {
            var cm2 = scope.ServiceProvider.GetRequiredService<ICm2Repository>();
            var packed = await cm2.GetUpcomingConsultsAsync(day, day, cardiologistId: null, ct);
            consults = packed.Consults;
        }

        _log.LogInformation("Daily screen found {Count} consult(s) for {Day}", consults.Count, day);

        if (consults.Count > 0)
        {
            await WaitForIdleJobAsync(ct);
            var ids = consults
                .Select(c => c.AppointmentId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(ResearchScreeningJobService.MaxBatchSize)
                .ToList();

            var (job, error, conflict) = _jobs.Start(
                new StartResearchScreeningJobRequest(ids, ForceRescreen: false));
            if (conflict)
            {
                _log.LogWarning("Daily screen deferred — another batch is running: {Error}", error);
                await WaitForIdleJobAsync(ct);
                (job, error, conflict) = _jobs.Start(
                    new StartResearchScreeningJobRequest(ids, ForceRescreen: false));
            }

            if (error is not null || conflict)
            {
                _log.LogWarning("Daily screen could not start: {Error}", error ?? "conflict");
            }
            else
            {
                _log.LogInformation("Daily screening job {JobId} started ({Total} consults)", job.Id, job.Total);
                await WaitForJobAsync(job.Id, ct);
                var finished = _jobs.GetCurrent();
                _log.LogInformation(
                    "Daily screening job finished: status={Status} done={Completed} skipped={Skipped} failed={Failed}",
                    finished.Status, finished.Completed, finished.Skipped, finished.Failed);
            }
        }

        var saved = await _results.GetManyAsync(consults.Select(c => c.AppointmentId), ct);
        var overlaid = consults.Select(row =>
        {
            if (!saved.TryGetValue(row.AppointmentId, out var s))
                return row;
            return row with
            {
                Status = s.Status,
                ScreenedAt = s.ScreenedAt,
                Studies = s.Studies,
                PatientRid = s.PatientRid ?? row.PatientRid,
                DocumentsReviewed = s.DocumentsReviewed ?? row.DocumentsReviewed,
                PrimaryLettersRid = s.PrimaryLettersRid ?? row.PrimaryLettersRid,
                PrimaryLetterPath = s.PrimaryLetterPath ?? row.PrimaryLetterPath,
                PrimaryLetterDate = s.PrimaryLetterDate ?? row.PrimaryLetterDate,
                SourceFileFingerprint = s.SourceFileFingerprint ?? row.SourceFileFingerprint,
            };
        }).ToList();

        var markdown = ResearchCandidateSlackReport.Build(overlaid, day);
        if (!_slackSettings.CurrentValue.IsConfigured)
            return;

        await using (var scope = _scopes.CreateAsyncScope())
        {
            var slack = scope.ServiceProvider.GetRequiredService<IResearchSlackClient>();
            var (ok, slackError) = await slack.PostCandidateReportAsync(markdown, ct);
            if (ok)
                _log.LogInformation("Daily candidate report posted to Slack for {Day}", day);
            else
                _log.LogError("Daily Slack post failed for {Day}: {Error}", day, slackError);
        }
    }

    private async Task WaitForIdleJobAsync(CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(30);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (!_jobs.GetCurrent().IsActive)
                return;
            await Task.Delay(PollInterval, ct);
        }
    }

    private async Task WaitForJobAsync(string jobId, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + JobWaitTimeout;
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var job = _jobs.GetCurrent();
            if (!string.Equals(job.Id, jobId, StringComparison.OrdinalIgnoreCase))
                return;
            if (!job.IsActive)
                return;
            await Task.Delay(PollInterval, ct);
        }

        _log.LogWarning("Timed out waiting for screening job {JobId}", jobId);
    }

    private static TimeOnly ClampTime(int hour, int minute) =>
        new(Math.Clamp(hour, 0, 23), Math.Clamp(minute, 0, 59));

    private static TimeSpan NextWakeDelay(
        ResearchDailySlackSettings settings,
        TimeZoneInfo tz,
        DateTime utcNow)
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(utcNow, tz);
        var today = DateOnly.FromDateTime(nowLocal);
        var scheduled = ClampTime(settings.Hour, settings.Minute);

        for (var add = 0; add <= 8; add++)
        {
            var day = today.AddDays(add);
            if (settings.WeekdaysOnly
                && day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            var localTarget = day.ToDateTime(scheduled);
            if (add == 0 && nowLocal >= localTarget)
                continue;

            var utcTarget = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(localTarget, DateTimeKind.Unspecified), tz);
            var delay = utcTarget - utcNow;
            return delay > TimeSpan.Zero ? delay : IdlePoll;
        }

        return IdlePoll;
    }

    private DateOnly? LoadLastRunDate()
    {
        try
        {
            if (!File.Exists(_lastRunPath))
                return null;
            var text = File.ReadAllText(_lastRunPath).Trim();
            return DateOnly.TryParse(text, out var d) ? d : null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not read daily Slack last-run file");
            return null;
        }
    }

    private void SaveLastRunDate(DateOnly day)
    {
        try
        {
            var dir = Path.GetDirectoryName(_lastRunPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(_lastRunPath, day.ToString("yyyy-MM-dd"));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not write daily Slack last-run file");
        }
    }
}
