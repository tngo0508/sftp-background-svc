using Serilog;
using SftpBackgroundService.Hosting;
using SftpBackgroundService.Options;
using SftpBackgroundService.Processing;
using SftpBackgroundService.Queue;
using SftpBackgroundService.Sftp;

// When hosted as a Windows Service the working directory is System32. Anchor it to the exe folder so
// appsettings.json and the relative "logs/" path in the Serilog config resolve next to the binary.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

// Bootstrap logger: captures anything that goes wrong before the real configuration is loaded.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = Host.CreateApplicationBuilder(args);

    // Replace the default Microsoft.Extensions.Logging providers with Serilog, configured from the "Serilog" section.
    builder.Services.AddSerilog((services, loggerConfiguration) => loggerConfiguration
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // Lets the same binary run as a console app (dotnet run) or as a Windows Service (sc.exe).
    builder.Services.AddWindowsService(options => options.ServiceName = "SftpBackgroundService");

    builder.Services.AddOptions<SftpOptions>()
        .BindConfiguration(SftpOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<SftpOptions>, SftpOptionsValidator>();

    builder.Services.AddOptions<ScheduleOptions>()
        .BindConfiguration(ScheduleOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<IBackgroundTaskQueue>(new DefaultBackgroundTaskQueue(capacity: 100));
    builder.Services.AddSingleton<ISftpDownloader, SftpDownloader>();
    builder.Services.AddSingleton<IFileProcessor, LoggingFileProcessor>();

    // Consumer first so the queue is being drained before the scheduler can enqueue anything.
    builder.Services.AddHostedService<QueuedHostedService>();
    builder.Services.AddHostedService<SftpDownloadService>();

    var host = builder.Build();
    host.Run();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Host terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}
