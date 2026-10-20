using pk3DS.Editors;

namespace pk3DS.Mac.Web;

/// <summary>Runs at most one project extraction at a time and retains its latest result for polling.</summary>
public sealed class ExtractionJobService(ILogger<ExtractionJobService> logger)
{
    private const long ProgressUpdateInterval = 256 * 1024;
    private readonly object gate = new();
    private readonly Dictionary<string, Job> jobs = new(StringComparer.Ordinal);
    private string? activeJobId;

    public ExtractProjectJobCreated? Start(ExtractProjectRequest request)
    {
        Job job;
        lock (gate)
        {
            if (activeJobId is not null && jobs.TryGetValue(activeJobId, out var active) &&
                active.Status is "pending" or "running")
                return null;

            job = new Job(Guid.NewGuid().ToString("N"));
            jobs.Add(job.Id, job);
            activeJobId = job.Id;

            foreach (var oldJob in jobs.Values
                         .Where(candidate => candidate.Status is "completed" or "failed")
                         .OrderByDescending(candidate => candidate.CreatedAt)
                         .Skip(20)
                         .ToArray())
                jobs.Remove(oldJob.Id);
        }

        _ = Task.Run(() => Execute(job, request));
        return new ExtractProjectJobCreated(job.Id, job.Status, $"/api/workspace/extract/jobs/{job.Id}");
    }

    public ExtractProjectJobStatus? Get(string jobId)
    {
        lock (gate)
        {
            if (!jobs.TryGetValue(jobId, out var job))
                return null;
            return Snapshot(job);
        }
    }

    private void Execute(Job job, ExtractProjectRequest request)
    {
        SetState(job, "running", "Iniciando extracción…", 0, 0);
        try
        {
            var result = ProjectTools.ExtractProject(request, progress => ReportProgress(job, progress));
            lock (gate)
            {
                job.Status = "completed";
                job.Message = result.Note;
                job.Current = 0;
                job.Total = 0;
                job.Result = result;
                activeJobId = null;
            }
        }
        catch (WorkspaceException ex)
        {
            SetFailed(job, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falló la tarea de extracción {JobId}", job.Id);
            SetFailed(job, "No pude completar la extracción. Verificá que el archivo esté desencriptado y completo.");
        }
    }

    private void ReportProgress(Job job, ExtractProjectProgress progress)
    {
        lock (gate)
        {
            if (job.Status != "running")
                return;

            var sameProgressRun = string.Equals(job.Message, progress.Message, StringComparison.Ordinal) &&
                                  job.Total == progress.Total;
            if (sameProgressRun && progress.Total > 0 && progress.Current < progress.Total &&
                progress.Current >= job.Current && progress.Current - job.Current < ProgressUpdateInterval)
                return;

            job.Message = progress.Message;
            job.Current = progress.Current;
            job.Total = progress.Total;
        }
    }

    private void SetState(Job job, string status, string message, long current, long total)
    {
        lock (gate)
        {
            job.Status = status;
            job.Message = message;
            job.Current = current;
            job.Total = total;
        }
    }

    private void SetFailed(Job job, string error)
    {
        lock (gate)
        {
            job.Status = "failed";
            job.Message = error;
            job.Error = error;
            job.Current = 0;
            job.Total = 0;
            activeJobId = null;
        }
    }

    private static ExtractProjectJobStatus Snapshot(Job job) => new(
        job.Id,
        job.Status,
        job.Message,
        job.Current,
        job.Total,
        job.Result,
        job.Error);

    private sealed class Job(string id)
    {
        public string Id { get; } = id;
        public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
        public string Status { get; set; } = "pending";
        public string Message { get; set; } = "En cola…";
        public long Current { get; set; }
        public long Total { get; set; }
        public ExtractProjectResponse? Result { get; set; }
        public string? Error { get; set; }
    }
}

/// <summary>Accepted response for an extraction job; poll <see cref="StatusUrl"/> for progress.</summary>
public sealed record ExtractProjectJobCreated(string JobId, string Status, string StatusUrl);

/// <summary>Current extraction state. Progress totals describe a stage or the current RomFS file.</summary>
public sealed record ExtractProjectJobStatus(
    string JobId,
    string Status,
    string Message,
    long Current,
    long Total,
    ExtractProjectResponse? Result,
    string? Error);
