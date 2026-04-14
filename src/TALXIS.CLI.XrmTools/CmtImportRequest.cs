namespace TALXIS.CLI.XrmTools;

/// <summary>
/// Request parameters for standalone CMT data import.
/// </summary>
public sealed record CmtImportRequest(
    /// <summary>Path to the CMT data package (.zip file or extracted folder containing data.xml and data_schema.xml).</summary>
    string DataPath,

    /// <summary>Dataverse connection string. Mutually exclusive with <see cref="EnvironmentUrl"/>.</summary>
    string? ConnectionString,

    /// <summary>Dataverse environment URL for interactive auth.</summary>
    string? EnvironmentUrl,

    /// <summary>Use device code flow instead of browser for interactive auth.</summary>
    bool DeviceCode,

    /// <summary>Number of parallel import connections. Defaults to 1.</summary>
    int ConnectionCount,

    /// <summary>Enable verbose CMT trace output.</summary>
    bool Verbose);
