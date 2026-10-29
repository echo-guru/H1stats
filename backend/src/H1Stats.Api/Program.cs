using H1Stats.Core.Configuration;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Modules;
using H1Stats.Infrastructure.Auth;
using H1Stats.Infrastructure.Data;
using H1Stats.Infrastructure.Llm;
using H1Stats.Infrastructure.Research;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<DatabaseSettings>(
    builder.Configuration.GetSection(DatabaseSettings.SectionName));
builder.Services.Configure<ResearchLlmSettings>(options =>
{
    builder.Configuration.GetSection(ResearchLlmSettings.SectionName).Bind(options);
    ResearchLlmSettings.ApplyEnvironmentOverrides(options);
});
builder.Services.Configure<ResearchSlackSettings>(options =>
{
    builder.Configuration.GetSection(ResearchSlackSettings.SectionName).Bind(options);
    ResearchSlackSettings.ApplyEnvironmentOverrides(options);
});
builder.Services.Configure<ResearchDailySlackSettings>(options =>
{
    builder.Configuration.GetSection(ResearchDailySlackSettings.SectionName).Bind(options);
    ResearchDailySlackSettings.ApplyEnvironmentOverrides(options);
});

// Module registry — add future modules here
builder.Services.AddSingleton<ModuleRegistry>(_ => new ModuleRegistry([
    new ClinicalModule(),
    new AdministrationModule(),
    new ResearchModule(),
]));

// Dependency injection
var usersFilePath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "users.json");
var studiesFilePath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "research-studies.json");
var screeningResultsFilePath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "research-screening-results.json");
builder.Services.AddSingleton<IAuthService>(_ => new AuthService(usersFilePath));
builder.Services.AddSingleton<IResearchStudyStore>(_ => new ResearchStudyStore(studiesFilePath));
builder.Services.AddSingleton<IResearchScreeningResultStore>(_ =>
    new ResearchScreeningResultStore(screeningResultsFilePath));
builder.Services.AddSingleton<IResearchDailyAutomationStore, ResearchDailyAutomationStore>();
builder.Services.AddScoped<ICm2Repository, Cm2Repository>();
builder.Services.AddSingleton<ICm2DevDatabase, Cm2DevDatabase>();
builder.Services.AddSingleton<IResearchScreeningJobService, ResearchScreeningJobService>();
        builder.Services.AddScoped<IResearchScreeningService>(sp =>
            new ResearchScreeningService(
                sp.GetRequiredService<IResearchLlmClient>(),
                sp.GetRequiredService<IResearchStudyStore>(),
                sp.GetRequiredService<IResearchScreeningResultStore>(),
                sp.GetRequiredService<ICm2Repository>(),
                sp.GetRequiredService<IOptions<ResearchLlmSettings>>()));
builder.Services.AddSingleton<IUserAdminService>(sp => (AuthService)sp.GetRequiredService<IAuthService>());
builder.Services.AddSingleton<IDatabaseConnectionService, DatabaseConnectionService>();
builder.Services.AddScoped<IPhysicianStatisticsRepository, PhysicianStatisticsRepository>();
builder.Services.AddScoped<ITeeIoeProgressRepository, TeeIoeProgressRepository>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddHttpClient<IResearchLlmClient, ResearchLlmClient>((sp, client) =>
{
    var settings = sp.GetRequiredService<IOptions<ResearchLlmSettings>>().Value;
    // Rely on per-request CancelAfter in ResearchLlmClient for clear timeout errors.
    // Keep HttpClient.Timeout as a hard ceiling slightly above the configured limit.
    var timeout = Math.Clamp(settings.TimeoutSeconds, 1, 3600) + 30;
    client.Timeout = TimeSpan.FromSeconds(timeout);
});
builder.Services.AddHttpClient<IResearchSlackClient, ResearchSlackClient>();
builder.Services.AddHostedService<ResearchDailySlackHostedService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var frontendUrl = builder.Configuration["FrontendUrl"] ?? "http://localhost:5180";
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(frontendUrl)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();

app.UseCors();
app.MapControllers();

app.Logger.LogInformation("H1Stats API starting on port 5002");
app.Run();
