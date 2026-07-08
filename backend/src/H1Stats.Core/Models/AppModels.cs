namespace H1Stats.Core.Models;

public static class AppModules
{
    public const string Dashboard = "dashboard";
    public const string Clinical = "clinical";
    public const string Administration = "administration";
    public const string Operations = "operations";
    public const string Quality = "quality";
    public const string Research = "research";
    public const string Finance = "finance";
}

public record UserAccount(
    string Username,
    string PasswordHash,
    bool IsAdmin,
    bool IsDisabled,
    IReadOnlyList<string> Modules,
    int FailedLoginAttempts = 0,
    DateTime? LockedUntil = null
);

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Username, bool IsAdmin, IReadOnlyList<string> Modules, string? Token);

public record UserDto(string Username, bool IsAdmin, bool IsDisabled);

public record CreateUserRequest(string Username, string Password, bool IsAdmin);

public record UpdateUserRequest(string? Password, bool? IsAdmin);

public record DashboardSummary(
    int StudiesToday,
    int StudiesThisWeek,
    int StudiesThisMonth,
    IReadOnlyList<NamedCount> TopPhysicians,
    IReadOnlyList<StudyMixItem> StudyMix,
    IReadOnlyList<ActivityItem> RecentActivity
);

public record NamedCount(string Name, int Count);

public record StudyMixItem(string Name, int Count, int Inpatient, int Outpatient);

public record ActivityItem(string Description, string At);

public record PhysicianStatRow(
    string Physician,
    string StudyType,
    int Total,
    int Outpatient,
    int Inpatient
);

public record PhysicianGroup(
    string Physician,
    IReadOnlyList<PhysicianStatRow> Rows,
    CountTriple Subtotal
);

public record CountTriple(int Total, int Outpatient, int Inpatient);

public record PhysicianStatisticsReport(
    IReadOnlyList<PhysicianGroup> Groups,
    CountTriple GrandTotal
);

public record ConnectionTestResult(bool Connected, string Message, DateTime? TestedAt = null);

public record HealthResponse(
    string Status,
    string Version,
    ConnectionTestResult SqlServer,
    string Uptime
);
