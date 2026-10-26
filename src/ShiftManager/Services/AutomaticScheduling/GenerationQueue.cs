using System.Threading.Channels;
using ShiftManager.Api;
using Microsoft.EntityFrameworkCore;
using ShiftManager.Data;
namespace ShiftManager.Services.AutomaticScheduling;

public sealed class GenerationJob(int year, int month, DateTime expires)
{
    public Guid Id { get; } = Guid.NewGuid();
    public int Year { get; } = year;
    public int Month { get; } = month;
    public DateTime ExpiresAt { get; } = expires;
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public CancellationTokenSource Cancellation { get; } = new();
    public SchedulingProblem? Problem { get; set; }
    private GenerationResponse response = new(Guid.Empty, year, month, "QUEUED", null, 0, false, false, null, expires, [], null);
    public GenerationResponse Response { get => Volatile.Read(ref response); set => Volatile.Write(ref response, value); }
}

public sealed class GenerationQueue(TimeProvider clock) : IDisposable
{
    private readonly object sync = new();
    private readonly Dictionary<Guid, GenerationJob> jobs = [];
    private readonly Channel<GenerationJob> queue = Channel.CreateBounded<GenerationJob>(8);
    public ChannelReader<GenerationJob> Reader => queue.Reader;
    public GenerationResponse Start(int year, int month)
    {
        AvailabilityService.MonthStart(year, month);
        lock (sync)
        {
            foreach (var expired in jobs.Values.Where(j => j.ExpiresAt <= clock.GetUtcNow().UtcDateTime).ToArray())
            { expired.Cancellation.Cancel(); jobs.Remove(expired.Id); }
            if (jobs.Values.Any(j => j.Year == year && j.Month == month && j.Response.State is "QUEUED" or "RUNNING")) throw new ApiException("GENERATION_BUSY", 409);
            if (jobs.Count >= 32) throw new ApiException("GENERATION_BUSY", 429);
            var job = new GenerationJob(year, month, clock.GetUtcNow().UtcDateTime.AddMinutes(30));
            job.Response = job.Response with { Id = job.Id };
            jobs.Add(job.Id, job);
            if (!queue.Writer.TryWrite(job)) { jobs.Remove(job.Id); throw new ApiException("GENERATION_BUSY", 429); }
            return job.Response;
        }
    }
    public GenerationJob Get(Guid id)
    {
        lock (sync)
        {
            if (!jobs.TryGetValue(id, out var job) || job.ExpiresAt <= clock.GetUtcNow().UtcDateTime) throw new ApiException("PREVIEW_EXPIRED", 410);
            return job;
        }
    }
    public async Task Cancel(Guid id)
    {
        var job = Get(id);
        await job.Gate.WaitAsync();
        try
        {
            if (job.Response.State == "SAVED") throw new ApiException("CONFLICT", 409);
            job.Cancellation.Cancel();
            job.Response = job.Response with { State = "CANCELED", Cells = [], Summary = null };
        }
        finally { job.Gate.Release(); }
    }
    public void Dispose() { lock (sync) { foreach (var j in jobs.Values) j.Cancellation.Cancel(); } }
}

public sealed class GenerationWorker(GenerationQueue queue, IServiceScopeFactory scopes, IConfiguration configuration,
    IScheduleOptimizer optimizer, ILogger<GenerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await job.Gate.WaitAsync(stoppingToken);
            try
            {
                if (job.Cancellation.IsCancellationRequested) continue;
                job.Response = job.Response with { State = "RUNNING" };
            }
            finally { job.Gate.Release(); }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, job.Cancellation.Token);
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                SchedulingProblem problem;
                await using (var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, linked.Token))
                {
                    problem = await scope.ServiceProvider.GetRequiredService<SchedulingInputs>().Read(job.Year, job.Month, linked.Token);
                    await tx.CommitAsync(linked.Token);
                }
                var seconds = Math.Clamp(configuration.GetValue("AutomaticScheduling:SolverSeconds", 30d), 0.01, 300);
                var result = await Task.Run(() => optimizer.Solve(problem, seconds, linked.Token), linked.Token);
                await job.Gate.WaitAsync(stoppingToken);
                try
                {
                    if (job.Cancellation.IsCancellationRequested) continue;
                    job.Problem = problem;
                    job.Response = job.Response with { State = result.Status is "OPTIMAL" or "FEASIBLE" ? "READY" : result.Status,
                        SolverStatus = result.Status, ProvenObjectives = result.ProvenObjectives, CoverageOptimal = result.ProvenObjectives >= 2,
                        Cells = result.Cells, Summary = result.Status is "OPTIMAL" or "FEASIBLE" ? StaffingSummary.From(result.Cells) : null };
                }
                finally { job.Gate.Release(); }
            }
            catch (OperationCanceledException) { job.Response = job.Response with { State = "CANCELED" }; }
            catch (Exception error)
            {
                logger.LogWarning("Automatic scheduling failed ({ExceptionType}) for run {RunId}", error.GetType().Name, job.Id);
                await job.Gate.WaitAsync(stoppingToken);
                try
                {
                    if (!job.Cancellation.IsCancellationRequested)
                        job.Response = job.Response with { State = "FAILED", ErrorCode = error is ApiException api ? api.Code : "INTERNAL_ERROR" };
                }
                finally { job.Gate.Release(); }
            }
        }
    }
}
