using System.Text.Json;

namespace ShiftManager.Tests;

internal static class TestTiming
{
    private static readonly object Gate = new();
    internal static void Record(string stage, double seconds)
    {
        var directory = Environment.GetEnvironmentVariable("TEST_TIMING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        lock (Gate)
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "fixtures.jsonl"),
                JsonSerializer.Serialize(new { Stage = stage, Seconds = seconds }) + Environment.NewLine);
        }
    }

    internal static void Failure(string action, Exception error)
    {
        var directory = Environment.GetEnvironmentVariable("TEST_TIMING_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory)) return;
        var types = new List<string>();
        for (Exception? current = error; current is not null; current = current.InnerException)
            types.Add(current.GetType().FullName ?? current.GetType().Name);
        lock (Gate)
        {
            Directory.CreateDirectory(directory);
            // Fixed metadata only: no exception messages, SQL, arguments or request bodies.
            File.AppendAllText(Path.Combine(directory, "failures.jsonl"), JsonSerializer.Serialize(new {
                Action = action, Types = types, ThreadPoolThreads = ThreadPool.ThreadCount,
                PendingWorkItems = ThreadPool.PendingWorkItemCount,
                Frames = new System.Diagnostics.StackTrace(error, false).GetFrames().Take(8)
                    .Select(f => f.GetMethod()?.DeclaringType?.FullName + "." + f.GetMethod()?.Name) }) + Environment.NewLine);
        }
    }
}
