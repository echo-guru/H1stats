using H1Stats.Core.Configuration;
using H1Stats.Core.Models;

namespace H1Stats.Core.Interfaces;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task LogoutAsync(string username, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListEnabledUsernamesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetFavoriteReportsAsync(string username, CancellationToken ct = default);
    Task<(IReadOnlyList<string>? Paths, string? Error)> SetFavoriteReportsAsync(
        string username, IReadOnlyList<string> paths, CancellationToken ct = default);
}

public interface IUserAdminService
{
    Task<IReadOnlyList<UserDto>> ListUsersAsync(CancellationToken ct = default);
    Task<(UserDto? User, string? Error)> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<(UserDto? User, string? Error)> UpdateUserAsync(string username, UpdateUserRequest request, CancellationToken ct = default);
    Task<(bool Deleted, string? Error)> DeleteUserAsync(string username, CancellationToken ct = default);
}

public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync(CancellationToken ct = default);
}

public interface IPhysicianStatisticsRepository
{
    Task<IReadOnlyList<string>> GetPhysiciansAsync(CancellationToken ct = default);
    Task<PhysicianStatisticsReport> GetReportAsync(DateOnly dateFrom, DateOnly dateTo, string? physician, CancellationToken ct = default);
}

/// <summary>
/// Past TEE/IOE diary bookings cross-checked against Syngo studies and CM2 TEST reports.
/// </summary>
public interface ITeeIoeProgressRepository
{
    Task<TeeIoeProgressResult> GetReportAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? cardiologistId = null,
        CancellationToken ct = default);
}

public interface ICm2Repository
{
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CardiologistOption>> GetCardiologistsAsync(CancellationToken ct = default);
    Task<PhysicianStatisticsReport> GetReportingDrReportAsync(
        DateOnly dateFrom, DateOnly dateTo, string? cardiologist, CancellationToken ct = default);
    Task<IReadOnlyList<SiteOption>> GetSitesAsync(CancellationToken ct = default);
    Task<TopReferringDoctorsReport> GetTopReferringDoctorsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        int topN,
        IReadOnlyList<string>? investigationTypes,
        string? site,
        string? ward = null,
        bool excludeH1Doctors = false,
        CancellationToken ct = default);
    Task<XmasMarketingReport> GetXmasMarketingReportAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        int topN,
        IReadOnlyList<string>? investigationTypes,
        string? site,
        string? ward = null,
        bool excludeH1Doctors = false,
        CancellationToken ct = default);
    Task<TopReferringPracticesReport> GetTopReferringPracticesAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        int topN,
        string? site,
        bool excludeHearts1st = false,
        CancellationToken ct = default,
        IReadOnlyList<string>? investigationTypes = null,
        string? ward = null,
        bool excludeH1Doctors = false,
        bool includeConsults = false);
    Task<ReferrersGrowthReport> GetReferrersGrowthByQuarterAsync(
        int year, int quarter, int topN, string? investigationType, string? sortBy, string? mode, string? site, int quarterSpan = 2, CancellationToken ct = default);
    Task<IReadOnlyList<ReferrerSearchHit>> SearchReferringDoctorsAsync(string query, CancellationToken ct = default);
    Task<IReadOnlyList<ReferrerSearchHit>> SearchReferringPracticesAsync(string query, CancellationToken ct = default);
    Task<PracticeDoctorsReport> GetPracticeDoctorsAsync(
        string practiceId, string? investigationType, CancellationToken ct = default);
    Task<ReferrerHistoryReport> GetReferrerHistoryAsync(
        string mode, string entityId, string? investigationType, DateOnly dateFrom, DateOnly dateTo, string? grain = null, CancellationToken ct = default);
    Task<ReferrerHistoryStudiesResult> GetReferrerHistoryStudiesAsync(
        string mode, string entityId, string investigationType, string periodKey,
        DateOnly dateFrom, DateOnly dateTo, string? grain = null, string? doctorId = null,
        CancellationToken ct = default);
    Task<StudyReportFile?> GetStudyReportFileAsync(string testRid, CancellationToken ct = default);
    Task OpenStudyReportAsync(string testRid, CancellationToken ct = default);
    Task<DrSummaryReport> GetDrSummaryAsync(int year, int month, CancellationToken ct = default);
    Task<DrSummaryReport> GetDrSummaryHistoryAsync(
        string? cardiologistId, int year, int month, CancellationToken ct = default);
    /// <summary>
    /// James Report: site volumes by calendar quarter for the selected date range and tests.
    /// </summary>
    Task<JamesReport> GetJamesReportAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        IReadOnlyList<string> testCodes,
        CancellationToken ct = default);
    /// <summary>
    /// Upcoming Initial / Follow-up consult diary bookings (SMS module pattern).
    /// </summary>
    Task<ResearchUpcomingConsultsResult> GetUpcomingConsultsAsync(
        DateOnly dateFrom, DateOnly dateTo, string? cardiologistId = null, CancellationToken ct = default);
    Task<ResearchUpcomingConsult?> GetUpcomingConsultByIdAsync(
        string appointmentId, CancellationToken ct = default);

    /// <summary>
    /// Unarrived pink (Cardiology) recalls from the Oracle diary.
    /// Blank date range = all unarrived; optional site and PAT_CARDIOLOGIST filters.
    /// </summary>
    Task<PinkRecallWorklistResult> GetUnarrivedPinkRecallsAsync(
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        string? site = null,
        string? cardiologistId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Consult-referral documents imported onto a patient with no unarrived pink/yellow recall.
    /// </summary>
    Task<UnactionedConsultReferralResult> GetUnactionedConsultReferralsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? cardiologistId = null,
        CancellationToken ct = default);
    Task<(StudyReportFile? File, string? Error)> GetUnactionedConsultReferralFileAsync(
        string documentId, CancellationToken ct = default);

    /// <summary>
    /// Past CTCA diary bookings with no imported test/results CTCA document. One row per patient.
    /// Date range is clamped to yesterday (future bookings excluded).
    /// </summary>
    Task<MissingCtcaDocResult> GetMissingCtcaDocsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? cardiologistId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Updates SCHEDULE_APPOINTMENT.APPOINTMENT_NOTE for a diary booking (max 2000 chars).
    /// </summary>
    Task<(bool Ok, string? Error)> UpdateAppointmentNoteAsync(
        string appointmentId, string? note, CancellationToken ct = default);
    Task<(bool Ok, string? Error, string? ServiceTypeId, string? ServiceTitle)> UpdateAppointmentServiceTypeAsync(
        string appointmentId, string serviceTypeId, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> ArriveAppointmentAsync(
        string appointmentId, CancellationToken ct = default);
    Task<(bool Ok, string? Error, DateOnly? Date, string? Time)> UpdateAppointmentScheduleAsync(
        string appointmentId, string date, string? time, CancellationToken ct = default);
    Task<AvailableDiarySlotsResult> GetAvailableDiarySlotsAsync(
        string appointmentId, string date, CancellationToken ct = default);

    /// <summary>
    /// Most recent previous consultation letter (CONSULT ⋈ LETTERS, LETTER_TYPE 6/7)
    /// before the upcoming appointment date. Semantic model: CM2_SEMANTIC_MODEL_UPDATED.
    /// </summary>
    Task<ResearchCm2LetterRef?> GetLatestConsultLetterAsync(
        string patientRid, DateOnly? beforeDate = null, CancellationToken ct = default);

    /// <summary>
    /// Latest investigation with a REPORT_PATH for the given TEST_REQUIRED code(s).
    /// Used only when consult-letter screening leaves criteria undetermined (TTE=1, stress=2/3).
    /// </summary>
    Task<ResearchCm2StudyReportRef?> GetLatestInvestigationReportAsync(
        string patientRid, IReadOnlyList<int> testRequiredCodes, CancellationToken ct = default);

    /// <summary>
    /// Resolves a CM2 relative path (LETTER_PATH / REPORT_PATH) under Cm2ReportRoot.
    /// Does not open or modify the file.
    /// </summary>
    bool TryResolveCm2FilePath(string relativePath, out string fullPath, out string error);
}

public interface IDatabaseConnectionService
{
    DatabaseSettings GetSettings();
    /// <summary>Live settings including password — for internal connection use only (never return to clients).</summary>
    DatabaseSettings GetLiveSettings();
    Task UpdateSettingsAsync(DatabaseSettings settings, CancellationToken ct = default);
    Task<ConnectionTestResult> TestConnectionAsync(DatabaseSettings? settings = null, CancellationToken ct = default);
    Task<IReadOnlyList<DatabaseConnectionTestResult>> TestAllConnectionsAsync(
        DatabaseSettings? settings = null, CancellationToken ct = default);
}

/// <summary>
/// Oracle access to CM2 DEV (CM2DEV). Used only by SandBox — never by production reports.
/// Credentials match the main CM2 Username/Password.
/// </summary>
public interface ICm2DevDatabase
{
    string Schema { get; }
    string EndpointLabel { get; }
    string ConnectionString { get; }
    Task<DatabaseConnectionTestResult> TestConnectionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SiteOption>> GetSitesAsync(CancellationToken ct = default);
    Task<PinkRecallWorklistResult> GetUnarrivedPinkRecallsAsync(
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        string? site = null,
        string? cardiologistId = null,
        CancellationToken ct = default);
    Task<UnactionedConsultReferralResult> GetUnactionedConsultReferralsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? cardiologistId = null,
        CancellationToken ct = default);
    Task<(StudyReportFile? File, string? Error)> GetUnactionedConsultReferralFileAsync(
        string documentId, CancellationToken ct = default);
    Task<MissingCtcaDocResult> GetMissingCtcaDocsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? cardiologistId = null,
        CancellationToken ct = default);
    Task<(bool Ok, string? Error)> UpdateAppointmentNoteAsync(
        string appointmentId, string? note, CancellationToken ct = default);
    Task<(bool Ok, string? Error, string? ServiceTypeId, string? ServiceTitle)> UpdateAppointmentServiceTypeAsync(
        string appointmentId, string serviceTypeId, CancellationToken ct = default);
    Task<(bool Ok, string? Error)> ArriveAppointmentAsync(
        string appointmentId, CancellationToken ct = default);
    Task<(bool Ok, string? Error, DateOnly? Date, string? Time)> UpdateAppointmentScheduleAsync(
        string appointmentId, string date, string? time, CancellationToken ct = default);
    Task<AvailableDiarySlotsResult> GetAvailableDiarySlotsAsync(
        string appointmentId, string date, CancellationToken ct = default);
    Task<UnassignedConsultPatientResult> GetUnassignedConsultPatientsAsync(
        int lookbackMonths = 12, CancellationToken ct = default);
    Task<(bool Ok, string? Error, string? CardiologistId, string? CardiologistName)> AssignPatientCardiologistAsync(
        string patientRid, string cardiologistId, CancellationToken ct = default);
}

/// <summary>Server-side client for the local research LLM. The browser never sees the API key.</summary>
public interface IResearchLlmClient
{
    Task<ResearchLlmStatus> GetStatusAsync(CancellationToken ct = default);
    Task<ResearchLlmChatResult> CompleteChatAsync(
        IReadOnlyList<ResearchLlmChatMessage> messages,
        string? modelId = null,
        CancellationToken ct = default);
}

/// <summary>Posts Research candidate reports to Slack. The browser never sees the webhook URL.</summary>
public interface IResearchSlackClient
{
    ResearchSlackStatus GetStatus();
    Task<(bool Ok, string? Error)> PostCandidateReportAsync(string markdownText, CancellationToken ct = default);
}

/// <summary>On/off gate for weekday auto-screen + Slack posting.</summary>
public interface IResearchDailyAutomationStore
{
    bool IsEnabled { get; }
    ResearchDailyAutomationStatus GetStatus();
    ResearchDailyAutomationStatus SetEnabled(bool enabled);
}

/// <summary>Screens a CM2 consult appointment against active studies via the local LLM.</summary>
public interface IResearchScreeningService
{
    Task<ResearchAppointmentScreeningResult> ScreenAppointmentAsync(
        string appointmentId, ScreenAppointmentRequest? request = null, CancellationToken ct = default);
}

/// <summary>One-at-a-time background batch over upcoming consults. Survives the HTTP request that started it.</summary>
public interface IResearchScreeningJobService
{
    ResearchScreeningJob GetCurrent();
    (ResearchScreeningJob Job, string? Error, bool Conflict) Start(StartResearchScreeningJobRequest request);
    ResearchScreeningJob Cancel();
}

/// <summary>Persisted appointment screening results (JSON file until a DB is warranted).</summary>
public interface IResearchScreeningResultStore
{
    Task<ResearchAppointmentScreeningResult?> GetAsync(string appointmentId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, ResearchAppointmentScreeningResult>> GetManyAsync(
        IEnumerable<string> appointmentIds, CancellationToken ct = default);
    Task UpsertAsync(ResearchAppointmentScreeningResult result, CancellationToken ct = default);
    Task<(bool Deleted, string? Error)> DeleteAsync(string appointmentId, CancellationToken ct = default);
}

/// <summary>Persisted research studies. Screening later runs a patient against all active studies.</summary>
public interface IResearchStudyStore
{
    Task<IReadOnlyList<ResearchStudy>> ListAsync(CancellationToken ct = default);
    Task<ResearchStudy?> GetAsync(string id, CancellationToken ct = default);
    Task<(ResearchStudy? Study, string? Error)> CreateAsync(SaveResearchStudyRequest request, CancellationToken ct = default);
    Task<(ResearchStudy? Study, string? Error)> UpdateAsync(string id, SaveResearchStudyRequest request, CancellationToken ct = default);
    Task<(bool Deleted, string? Error)> DeleteAsync(string id, CancellationToken ct = default);
}
