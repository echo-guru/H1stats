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
    DateTime? LockedUntil = null,
    IReadOnlyList<string>? FavoriteReportPaths = null,
    bool IsSuperAdmin = false
);

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Username, bool IsAdmin, bool IsSuperAdmin, IReadOnlyList<string> Modules, string? Token);

public record UserDto(string Username, bool IsAdmin, bool IsDisabled, bool IsSuperAdmin = false);

public record CreateUserRequest(
    string Username,
    string Password,
    bool IsAdmin,
    bool IsSuperAdmin = false,
    string? ActorUsername = null);

public record UpdateUserRequest(
    string? Password,
    bool? IsAdmin,
    bool? IsDisabled,
    string? NewUsername,
    bool? IsSuperAdmin = null,
    string? ActorUsername = null);

public record SetFavoriteReportsRequest(string Username, IReadOnlyList<string> Paths);

public static class ClinicalReportCatalog
{
    public static readonly IReadOnlySet<string> Paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "/clinical/physician-statistics",
        "/clinical/reporting-dr-cm2",
        "/clinical/dr-summary-cm2",
        "/clinical/james-report",
        "/clinical/tee-ioe-progress",
        "/clinical/top-referring-doctors-cm2",
        "/clinical/xmas-marketing",
        "/clinical/top-referring-practices-cm2",
        "/clinical/xmas-marketing-by-practice",
        "/clinical/referrers-growth-cm2",
        "/clinical/referrers-decline-cm2",
        "/clinical/referrer-history-cm2",
    };

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? paths) =>
        (paths ?? [])
            .Select(p => p?.Trim() ?? "")
            .Where(p => Paths.Contains(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public record CardiologistOption(string Id, string DisplayName);

public record SiteOption(string Id, string DisplayName);

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

public record ReferringDoctorStatRow(
    string PersonId,
    string DisplayName,
    string InvestigationType,
    int Total,
    int Outpatient,
    int Inpatient
);

public record ReferringDoctorGroup(
    int Rank,
    string PersonId,
    string DisplayName,
    IReadOnlyList<ReferringDoctorStatRow> Rows,
    CountTriple Subtotal
);

public record TopReferringDoctorsReport(
    IReadOnlyList<ReferringDoctorGroup> Groups,
    CountTriple GrandTotal,
    int TopN,
    string? InvestigationTypeFilter = null,
    string? SiteFilter = null,
    string? WardFilter = null,
    bool ExcludeH1Doctors = false
);

public record XmasMarketingRow(
    int Rank,
    string PersonId,
    string DisplayName,
    int Total,
    string? PracticeName,
    string? PracticeAddress,
    string? AddressStreet1 = null,
    string? AddressStreet2 = null,
    string? AddressSuburb = null,
    string? AddressPostcode = null
);

public record XmasMarketingReport(
    IReadOnlyList<XmasMarketingRow> Rows,
    int GrandTotal,
    int TopN,
    string? InvestigationTypeFilter = null,
    string? SiteFilter = null,
    string? WardFilter = null,
    bool ExcludeH1Doctors = false
);

public record ReferringPracticeTestRow(
    string InvestigationType,
    int Total,
    int Outpatient,
    int Inpatient
);

public record ReferringPracticeDoctor(
    string ProviderKey,
    string DisplayName,
    string? ProviderNumber,
    IReadOnlyList<ReferringPracticeTestRow> Rows,
    CountTriple Subtotal
);

public record ReferringPracticeGroup(
    int Rank,
    string PracticeId,
    string PracticeName,
    string? Suburb,
    IReadOnlyList<ReferringPracticeDoctor> Doctors,
    CountTriple Subtotal,
    string? AddressStreet1 = null,
    string? AddressStreet2 = null,
    string? AddressSuburb = null,
    string? AddressPostcode = null
);

public record TopReferringPracticesReport(
    IReadOnlyList<ReferringPracticeGroup> Groups,
    CountTriple GrandTotal,
    int TopN,
    string? SiteFilter = null,
    string? InvestigationTypeFilter = null,
    string? WardFilter = null,
    bool ExcludeH1Doctors = false
);

public record ReferrerQuarterColumn(string Key, string Label);

public record ReferrerGrowthTestRow(
    string InvestigationType,
    int PreviousTotal,
    int CurrentTotal,
    int Growth,
    decimal? GrowthPercent,
    IReadOnlyList<int> QuarterTotals
);

public record ReferrerGrowthRow(
    int Rank,
    string PersonId,
    string DisplayName,
    int PreviousTotal,
    int CurrentTotal,
    int Growth,
    decimal? GrowthPercent,
    IReadOnlyList<ReferrerGrowthTestRow> TestRows,
    IReadOnlyList<int> QuarterTotals
);

public record ReferrersGrowthReport(
    string CurrentQuarterLabel,
    string PreviousQuarterLabel,
    DateOnly CurrentFrom,
    DateOnly CurrentTo,
    DateOnly PreviousFrom,
    DateOnly PreviousTo,
    IReadOnlyList<ReferrerGrowthRow> Rows,
    int TopN,
    string? InvestigationTypeFilter = null,
    string SortBy = "growth",
    string Mode = "growth",
    string? SiteFilter = null,
    IReadOnlyList<ReferrerQuarterColumn>? Quarters = null
);

public record ReferrerSearchHit(
    string Id,
    string DisplayName,
    string? SecondaryText = null);

public record PracticeDoctorQuarter(
    string Key,
    string Label,
    int Total,
    IReadOnlyList<ReferrerHistoryTestRow> TestRows);

public record PracticeDoctorRow(
    string DoctorId,
    string DisplayName,
    int YearTotal,
    IReadOnlyList<PracticeDoctorQuarter> Quarters);

public record PracticeDoctorsReport(
    string PracticeId,
    string DisplayName,
    string? Suburb,
    DateOnly YearFrom,
    DateOnly YearTo,
    DateOnly QuarterFrom,
    DateOnly QuarterTo,
    IReadOnlyList<PracticeDoctorRow> Doctors,
    string? InvestigationTypeFilter = null);

public record ReferrerHistoryTestRow(
    string InvestigationType,
    int Total,
    int Outpatient,
    int Inpatient);

public record ReferrerHistoryDoctorRow(
    string DoctorId,
    string DisplayName,
    int Total,
    int Outpatient,
    int Inpatient,
    IReadOnlyList<ReferrerHistoryTestRow> TestRows);

public record ReferrerHistoryMonthRow(
    string YearMonth,
    string Label,
    int Total,
    int Outpatient,
    int Inpatient,
    IReadOnlyList<ReferrerHistoryTestRow> TestRows,
    IReadOnlyList<ReferrerHistoryDoctorRow> DoctorRows);

public record DrSummaryColumn(string Id, string Initials, string DisplayName);

public record DrSummaryRow(
    string Key,
    string Label,
    string Kind,
    IReadOnlyList<int> Counts);

public record DrSummaryReport(
    int Year,
    int Month,
    string Label,
    DateOnly DateFrom,
    DateOnly DateToExclusive,
    IReadOnlyList<DrSummaryColumn> Columns,
    IReadOnlyList<DrSummaryRow> Rows,
    int UnassignedDccs = 0);

/// <summary>One selected test column on the James Report. Counts in each row follow this order.</summary>
public record JamesReportTestColumn(string Code, string DisplayName, string ShortName);

/// <summary>One calendar quarter of site volume for the James Report.</summary>
public record JamesReportQuarterRow(
    int Year,
    int Quarter,
    string Label,
    IReadOnlyList<int> Counts);

/// <summary>Year block with quarter rows and year totals. Partial when the range does not cover the whole year.</summary>
public record JamesReportYearBlock(
    int Year,
    bool Partial,
    IReadOnlyList<JamesReportQuarterRow> Quarters,
    IReadOnlyList<int> Totals);

public record JamesReportSiteSection(
    string SiteId,
    string SiteName,
    IReadOnlyList<JamesReportYearBlock> Years);

/// <summary>
/// Site volumes by calendar quarter for a chosen date range and tests
/// (Greenslopes, Cleveland, Toowoomba).
/// </summary>
public record JamesReport(
    DateOnly DateFrom,
    DateOnly DateTo,
    IReadOnlyList<JamesReportTestColumn> Tests,
    IReadOnlyList<JamesReportSiteSection> Sites);

public record ReferrerHistoryReport(
    string Mode,
    string EntityId,
    string DisplayName,
    string? Suburb,
    DateOnly DateFrom,
    DateOnly DateTo,
    IReadOnlyList<string> CombinedSites,
    IReadOnlyList<ReferrerHistoryMonthRow> Months,
    CountTriple GrandTotal,
    string? InvestigationTypeFilter = null,
    string Grain = "month");

public record ReferrerHistoryStudyRow(
    string PatientCmId,
    string PatientName,
    DateOnly StudyDate,
    string TestRid,
    bool HasReport,
    string? WordUri = null);

public record ReferrerHistoryStudiesResult(
    string Mode,
    string EntityId,
    string DisplayName,
    string PeriodKey,
    string PeriodLabel,
    string InvestigationType,
    string? DoctorId,
    string? DoctorName,
    IReadOnlyList<ReferrerHistoryStudyRow> Studies);

public record StudyReportFile(string FullPath, string FileName, string ContentType);

public record ConnectionTestResult(bool Connected, string Message, DateTime? TestedAt = null);

public record DatabaseConnectionTestResult(
    string Database,
    bool Connected,
    string Message,
    DateTime? TestedAt = null);

public record HealthResponse(
    string Status,
    string Version,
    ConnectionTestResult SqlServer,
    string Uptime
);

/// <summary>Local LLM reachability. Never includes the API key.</summary>
public record ResearchLlmStatus(
    bool Online,
    string Label,
    string? ModelId,
    int ContextSize,
    string Message
);

/// <summary>Whether the Research Slack Incoming Webhook is configured. Never includes the URL.</summary>
public record ResearchSlackStatus(
    bool Configured,
    string Message
);

/// <summary>Weekday auto-screen + Slack schedule status. Never includes secrets.</summary>
public record ResearchDailyAutomationStatus(
    bool Enabled,
    int Hour,
    int Minute,
    bool WeekdaysOnly,
    string TimeZoneId,
    string Source,
    string Message
);

public record SetResearchDailyAutomationRequest(bool Enabled);
