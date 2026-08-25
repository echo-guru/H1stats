namespace H1Stats.Core.Configuration;

/// <summary>Read-only database connection settings — SQL Server (H1PACS) and Oracle (CM2).</summary>
public class DatabaseSettings
{
    public const string SectionName = "Database";

    /// <summary>H1PACS SQL Server hosting MVF and AcusonDB.</summary>
    public string Server { get; set; } = "192.168.12.205";

    /// <summary>Oracle server host for CM2. Falls back to <see cref="Server"/> when empty.</summary>
    public string Cm2Server { get; set; } = "";

    /// <summary>Oracle listener port for CM2 (default 1521).</summary>
    public string Cm2Port { get; set; } = "1521";

    public string MvfDatabase { get; set; } = "mvf";
    public string AcusonDatabase { get; set; } = "AcusonDB";

    /// <summary>Oracle SID for CM2 (e.g. CM19).</summary>
    public string Cm2Sid { get; set; } = "CM19";

    /// <summary>Oracle schema owning TEST / PATIENT tables.</summary>
    public string Cm2Schema { get; set; } = "HEARTS1ST";

    public string Username { get; set; } = "h1stats";
    public string Password { get; set; } = "";

    /// <summary>
    /// Folder CM2 relative report paths are resolved against (same as <c>mapped_drive</c> in CM2 config, typically <c>T:\</c>).
    /// <c>TEST.REPORT_PATH</c> values look like <c>\reports\ttx_echo\2003\{rid}.rtf</c>.
    /// </summary>
    public string Cm2ReportRoot { get; set; } = @"T:\";

    /// <summary>
    /// Path to WinWord.exe used to open CM2 RTF reports (same as <c>msword</c> in CM2 config).
    /// </summary>
    public string Cm2WordPath { get; set; } =
        @"C:\Program Files (x86)\Microsoft Office\OFFICE11\Winword.exe";

    public string EffectiveCm2Server =>
        string.IsNullOrWhiteSpace(Cm2Server) ? Server : Cm2Server.Trim();

    public string MvfConnectionString =>
        BuildSqlConnectionString(Server, MvfDatabase);

    public string AcusonConnectionString =>
        BuildSqlConnectionString(Server, AcusonDatabase);

    public string Cm2OracleConnectionString
    {
        get
        {
            var host = EffectiveCm2Server;
            var port = string.IsNullOrWhiteSpace(Cm2Port) ? "1521" : Cm2Port.Trim();
            var sid = string.IsNullOrWhiteSpace(Cm2Sid) ? "CM19" : Cm2Sid.Trim();
            return
                $"User Id={Username};Password={Password};" +
                $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))" +
                $"(CONNECT_DATA=(SID={sid})));Connection Timeout=5;";
        }
    }

    private string BuildSqlConnectionString(string server, string database) =>
        $"Server={server};Database={database};User Id={Username};Password={Password};TrustServerCertificate=True;ApplicationIntent=ReadOnly;Encrypt=False;";
}
