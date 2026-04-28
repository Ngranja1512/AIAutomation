using OpenClawIntegration.Models;
using OpenClawIntegration.Services;
using OpenClawIntegration.Workers;

var runOnce = args.Contains("--run-once");

var builder = Host.CreateApplicationBuilder(args);

// Bind configuration
builder.Services.Configure<AppSettings>(
    builder.Configuration.GetSection("OpenClawIntegration"));

// Register HTTP clients
builder.Services.AddHttpClient<ICopilotService, CopilotService>();
builder.Services.AddHttpClient<IOpenClawService, OpenClawService>();

// Register other services
builder.Services.AddSingleton<IEmailService, EmailService>();

if (runOnce)
{
    // One-shot mode: run the summary cycle once and exit.
    // Used by the GitHub Actions scheduled workflow.
    builder.Services.AddHostedService<OneShotWorker>();
}
else
{
    // Daemon mode: run on the configured cron schedule indefinitely.
    builder.Services.AddHostedService<SummaryWorker>();
}

var host = builder.Build();
host.Run();
