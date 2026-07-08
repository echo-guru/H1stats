namespace H1Stats.Core.Configuration;

/// <summary>Read-only SQL Server connection settings (H1PACS).</summary>
public class DatabaseSettings
{
    public const string SectionName = "Database";

    public string Server { get; set; } = "192.168.12.205";
    public string MvfDatabase { get; set; } = "mvf";
    public string AcusonDatabase { get; set; } = "AcusonDB";
    public string Username { get; set; } = "h1stats";
    public string Password { get; set; } = "";

    public string MvfConnectionString =>
        $"Server={Server};Database={MvfDatabase};User Id={Username};Password={Password};TrustServerCertificate=True;ApplicationIntent=ReadOnly;";

    public string AcusonConnectionString =>
        $"Server={Server};Database={AcusonDatabase};User Id={Username};Password={Password};TrustServerCertificate=True;ApplicationIntent=ReadOnly;";
}
