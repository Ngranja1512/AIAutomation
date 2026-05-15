using CopilotDigest.Models;
using CopilotDigest.Services;
using CopilotDigest.Workers;

var runOnce = args.Contains("--run-once");

var builder = Host.CreateApplicationBuilder(args);

// Load local overrides (gitignored — never committed). Place secrets here for local dev.
// In CI/CD, secrets are injected via environment variables instead.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Bind configuration
builder.Services.Configure<AppSettings>(
    builder.Configuration.GetSection("CopilotDigest"));

// Register HTTP clients
builder.Services.AddHttpClient<ICopilotService, CopilotService>(client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddHttpClient<IFreeMarketDataService, FreeMarketDataService>();
builder.Services.AddHttpClient<IMarketNewsService, YahooFinanceNewsService>();
builder.Services.AddHttpClient<IInsiderDataService, SecEdgarInsiderDataService>();
builder.Services.AddHttpClient<IFinancialDataService, AlphaVantageFinancialDataService>();

// Register other services
builder.Services.AddSingleton<IFinancePromptEnricher, FinancePromptEnricher>();
builder.Services.AddSingleton<IResearchService, ResearchService>();
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
