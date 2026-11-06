using SftpBackgroundService.Hosting;
using SftpBackgroundService.Options;
using SftpBackgroundService.Processing;
using SftpBackgroundService.Queue;
using SftpBackgroundService.Sftp;

var builder = Host.CreateApplicationBuilder(args);

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
