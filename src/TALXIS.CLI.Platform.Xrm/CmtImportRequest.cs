namespace TALXIS.CLI.Platform.Xrm;

/// <summary>
/// Identity-neutral request record for <see cref="CmtImportRunner"/>. The
/// connection string is passed as a separate parameter on
/// <see cref="CmtImportRunner.RunAsync"/> so that the request object — which
/// may be logged, serialized, or stored in flight — never carries secrets.
/// This mirrors <see cref="PackageDeployerRequest"/>.
/// </summary>
public sealed record CmtImportRequest(
    /// <summary>Path to the CMT data package (.zip file or extracted folder containing data.xml and data_schema.xml).</summary>
    string DataPath,

    /// <summary>Number of parallel import connections. Defaults to 1.</summary>
    int ConnectionCount,

    /// <summary>Enable verbose CMT trace output.</summary>
    bool Verbose);
