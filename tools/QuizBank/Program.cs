using System.Runtime.InteropServices;

// Ctrl+C and SIGTERM cancel the run instead of ending the process, so run-checks still removes its container.
using var cancellation = new CancellationTokenSource();
using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Cancel);
using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Cancel);

return await QuizBank.Cli.RunAsync(args, Console.Out, Console.Error, cancellationToken: cancellation.Token);

void Cancel(PosixSignalContext context)
{
    context.Cancel = true;
    cancellation.Cancel();
}
