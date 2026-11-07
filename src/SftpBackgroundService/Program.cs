using Serilog;
using SftpBackgroundService;
using SftpBackgroundService.Options;
using SftpBackgroundService.Processing;
using SftpBackgroundService.Sftp;

// Task Scheduler starts the process with an arbitrary working directory. Anchor it to the exe folder so
// appsettings.json and the relative "logs/" path in the Serilog config resolve next to the binary.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

// Bootstrap logger: captures anything that goes wrong before the real configuration is loaded.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services.AddOptions<SftpOptions>()
        .BindConfiguration(SftpOptions.SectionName)
        .ValidateDataAnnotations();
    builder.Services.AddSingleton<IValidateOptions<SftpOptions>, SftpOptionsValidator>();

    builder.Services.AddSingleton<ISftpDownloader, SftpDownloader>();
    builder.Services.AddSingleton<IFileProcessor, LoggingFileProcessor>();
    builder.Services.AddSingleton<SftpDownloadRunner>();

    // The host is used only as a container for configuration, logging and DI. It is never started:
    // this process does one run and exits, and Task Scheduler decides when the next run happens.
    using var host = builder.Build();

    // Options are validated lazily on first access. Touch them here so a bad config exits with
    // StartupFailed (3) instead of surfacing inside the run as a download failure (1).
    _ = host.Services.GetRequiredService<IOptions<SftpOptions>>().Value;

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    var runner = host.Services.GetRequiredService<SftpDownloadRunner>();
    return await runner.RunAsync(cts.Token);
}
catch (OperationCanceledException)
{
    Log.Warning("Run cancelled.");
    return ExitCodes.Cancelled;
}
catch (Exception ex)
{
    // Includes OptionsValidationException for bad or missing configuration.
    Log.Fatal(ex, "Run aborted before it could start.");
    return ExitCodes.StartupFailed;
}
finally
{
    Log.CloseAndFlush();
}
