using RMS.Infrastructure;
using RMS.Infrastructure.Configuration;
using RMS.Scheduler;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,

    // A Windows service starts in C:\Windows\System32; the settings files sit next to the program.
    ContentRootPath = AppContext.BaseDirectory,
});

// Machine-specific settings that should stay out of source control, as in the web application.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Any setting written as "env:NAME" is read from environment variable NAME.
builder.Configuration.ResolveEnvironmentReferences();

// Runs as a Windows service when started by the Service Control Manager, and as a console program otherwise
// (dotnet run, or double-clicking the exe), which is handy for testing.
builder.Services.AddWindowsService(options => options.ServiceName = "RMS.Scheduler");

builder.Services.AddReportsManagement(builder.Configuration, builder.Environment);
builder.Services.AddReportScheduler(builder.Configuration);
builder.Services.AddHostedService<SchedulerWorker>();

builder.Build().Run();
