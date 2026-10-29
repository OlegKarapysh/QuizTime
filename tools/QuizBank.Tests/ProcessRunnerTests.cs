using System.Diagnostics;

namespace QuizBank.Tests;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task Output_is_returned_with_the_exit_code()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Runs sh.");

        var result = await RunAsync("echo out; echo err >&2; exit 3", TimeSpan.FromMinutes(1));

        Assert.Equal(new ProcessResult(3, "out\n", "err\n", TimedOut: false), result);
    }

    [Fact]
    public async Task Timeout_also_covers_output_a_child_keeps_open()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Runs sh.");
        var watch = Stopwatch.StartNew();

        // sh exits at once, but the background sleep inherits stdout and holds it open for 20 seconds.
        var result = await RunAsync("sleep 20 & echo started", TimeSpan.FromSeconds(1));

        Assert.True(result.TimedOut);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Took {watch.Elapsed}.");
    }

    private static Task<ProcessResult> RunAsync(string script, TimeSpan timeout) =>
        ProcessRunner.RunAsync("sh", ["-c", script], ".", timeout, TestContext.Current.CancellationToken);
}
