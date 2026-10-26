using ShiftManager.Api;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;
public class GenerationQueueTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [Fact]
    public async Task Concurrent_duplicate_requests_have_one_winner_and_cancel_allows_retry()
    {
        using var queue = new GenerationQueue(TimeProvider.System);
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => {
            try { return queue.Start(2026, 10); } catch (ApiException e) { Assert.Equal("GENERATION_BUSY", e.Code); return null; }
        })));
        var first = Assert.Single(results, r => r != null);
        await queue.Cancel(first!.Id);
        Assert.Equal("CANCELED", queue.Get(first.Id).Response.State);
        Assert.Equal("QUEUED", queue.Start(2026, 10).State);
    }
    [Fact]
    public void Expired_preview_is_rejected()
    {
        var clock = new Clock(); using var queue = new GenerationQueue(clock);
        var first = queue.Start(2026, 10); clock.Now = clock.Now.AddMinutes(31);
        Assert.Equal("PREVIEW_EXPIRED", Assert.Throws<ApiException>(() => queue.Get(first.Id)).Code);
    }
    [Fact]
    public void Queue_is_bounded_and_does_not_accept_unlimited_work()
    {
        using var queue = new GenerationQueue(TimeProvider.System);
        for (var month = 1; month <= 8; month++) queue.Start(2026, month);
        Assert.Equal("GENERATION_BUSY", Assert.Throws<ApiException>(() => queue.Start(2026, 9)).Code);
    }
}
