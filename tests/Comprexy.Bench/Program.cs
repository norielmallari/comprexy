using Comprexy.Bench.Cli;
using Comprexy.Bench.Publishing;
using Comprexy.Bench.Reporting;
using Comprexy.Bench.Running;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    Console.Error.WriteLine("\ncancelling; tearing down bench hosts…");
    cancellation.Cancel();
};

try
{
    var options = BenchCommandLine.Parse(args);

    return options.Command switch
    {
        BenchCommand.Run => await BenchRunCommand.ExecuteAsync(options, cancellation.Token),
        BenchCommand.Report => await BenchReportCommand.ExecuteAsync(options, cancellation.Token),
        BenchCommand.Publish => await BenchPublishCommand.ExecuteAsync(options, cancellation.Token),
        _ => PrintUsage()
    };
}
catch (BenchUsageException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    Console.Error.WriteLine();
    PrintUsage();
    return 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("bench cancelled.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static int PrintUsage()
{
    Console.Error.WriteLine(BenchCommandLine.Usage);
    return 0;
}
