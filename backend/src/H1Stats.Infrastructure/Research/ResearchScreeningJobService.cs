using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace H1Stats.Infrastructure.Research;

/// <summary>
/// Runs consult screening sequentially in the background so the browser is not held open
/// for the full LLM batch. Only one job at a time — the local LLM is not parallel.
/// </summary>
public sealed class ResearchScreeningJobService : IResearchScreeningJobService
{
    public const int MaxBatchSize = 200;

    private readonly IServiceScopeFactory _scopes;
    private readonly object _gate = new();
    private ResearchScreeningJob _job = ResearchScreeningJob.Idle();
    private CancellationTokenSource? _cts;

    public ResearchScreeningJobService(IServiceScopeFactory scopes)
    {
        _scopes = scopes;
    }

    public ResearchScreeningJob GetCurrent()
    {
        lock (_gate)
            return Clone(_job);
    }

    public (ResearchScreeningJob Job, string? Error, bool Conflict) Start(StartResearchScreeningJobRequest request)
    {
        var ids = (request.AppointmentIds ?? [])
            .Select(id => id?.Trim() ?? "")
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (ids.Count == 0)
            return (GetCurrent(), "Select at least one consult to screen.", false);
        if (ids.Count > MaxBatchSize)
            return (GetCurrent(), $"A batch cannot exceed {MaxBatchSize} consults.", false);

        CancellationTokenSource cts;
        ResearchScreeningJob started;
        lock (_gate)
        {
            if (_job.IsActive)
                return (Clone(_job), "A screening batch is already running.", true);

            _cts?.Dispose();
            cts = new CancellationTokenSource();
            _cts = cts;
            started = new ResearchScreeningJob(
                Guid.NewGuid().ToString("N"),
                ResearchScreeningJobStatus.Running,
                ids.Count,
                0,
                0,
                0,
                request.ForceRescreen,
                DateTime.UtcNow,
                null,
                ids[0],
                null,
                ids.Select(id => new ResearchScreeningJobItem(id, ResearchScreeningJobItemStatus.Pending)).ToList());
            _job = started;
        }

        _ = Task.Run(() => RunAsync(started.Id, ids, request.ForceRescreen, cts.Token));
        return (Clone(started), null, false);
    }

    public ResearchScreeningJob Cancel()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            if (_job.IsActive)
            {
                _job = _job with
                {
                    Error = "Cancellation requested. The consult currently with the LLM may still finish.",
                };
            }

            return Clone(_job);
        }
    }

    private async Task RunAsync(
        string jobId,
        IReadOnlyList<string> appointmentIds,
        bool forceRescreen,
        CancellationToken ct)
    {
        try
        {
            for (var i = 0; i < appointmentIds.Count; i++)
            {
                if (ct.IsCancellationRequested)
                    break;

                var appointmentId = appointmentIds[i];
                Update(jobId, job =>
                {
                    var items = job.Items.ToList();
                    items[i] = items[i] with { Status = ResearchScreeningJobItemStatus.Running };
                    return job with
                    {
                        CurrentAppointmentId = appointmentId,
                        Items = items,
                    };
                });

                try
                {
                    await using var scope = _scopes.CreateAsyncScope();
                    var screening = scope.ServiceProvider.GetRequiredService<IResearchScreeningService>();
                    var result = await screening.ScreenAppointmentAsync(
                        appointmentId,
                        new ScreenAppointmentRequest(forceRescreen),
                        ct);

                    var skipped = result.AlreadyReviewedSameSource && !forceRescreen;
                    Update(jobId, job =>
                    {
                        var items = job.Items.ToList();
                        items[i] = new ResearchScreeningJobItem(
                            appointmentId,
                            skipped ? ResearchScreeningJobItemStatus.Skipped : ResearchScreeningJobItemStatus.Done,
                            null,
                            result.Status,
                            result.ScreenedAt,
                            result.AlreadyReviewedSameSource);
                        return job with
                        {
                            Completed = job.Completed + (skipped ? 0 : 1),
                            Skipped = job.Skipped + (skipped ? 1 : 0),
                            Items = items,
                        };
                    });
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Update(jobId, job =>
                    {
                        var items = job.Items.ToList();
                        items[i] = items[i] with
                        {
                            Status = ResearchScreeningJobItemStatus.Failed,
                            Error = ex.Message,
                        };
                        return job with
                        {
                            Failed = job.Failed + 1,
                            Items = items,
                        };
                    });
                }
            }

            Update(jobId, job => job with
            {
                Status = ct.IsCancellationRequested
                    ? ResearchScreeningJobStatus.Cancelled
                    : ResearchScreeningJobStatus.Completed,
                FinishedAt = DateTime.UtcNow,
                CurrentAppointmentId = null,
                Error = ct.IsCancellationRequested ? "Batch cancelled." : job.Error,
            });
        }
        catch (Exception ex)
        {
            Update(jobId, job => job with
            {
                Status = ResearchScreeningJobStatus.Failed,
                FinishedAt = DateTime.UtcNow,
                CurrentAppointmentId = null,
                Error = ex.Message,
            });
        }
    }

    private void Update(string jobId, Func<ResearchScreeningJob, ResearchScreeningJob> mutate)
    {
        lock (_gate)
        {
            if (!string.Equals(_job.Id, jobId, StringComparison.Ordinal))
                return;
            _job = mutate(_job);
        }
    }

    private static ResearchScreeningJob Clone(ResearchScreeningJob job) =>
        job with { Items = job.Items.ToList() };
}
