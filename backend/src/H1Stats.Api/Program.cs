using H1Stats.Core.Configuration;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Modules;
using H1Stats.Infrastructure.Auth;
using H1Stats.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<DatabaseSettings>(
    builder.Configuration.GetSection(DatabaseSettings.SectionName));

// Module registry — add future modules here
builder.Services.AddSingleton<ModuleRegistry>(_ => new ModuleRegistry([
    new ClinicalModule(),
    new AdministrationModule(),
]));

// Dependency injection
builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddSingleton<IUserAdminService>(sp => (AuthService)sp.GetRequiredService<IAuthService>());
builder.Services.AddSingleton<IDatabaseConnectionService, DatabaseConnectionService>();
builder.Services.AddScoped<IPhysicianStatisticsRepository, PhysicianStatisticsRepository>();
builder.Services.AddScoped<IDashboardService, DashboardService>();

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
