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

    /// <summary>Enable batch mode for import (ExecuteMultiple/UpsertMultiple).</summary>
    bool BatchMode,

    /// <summary>Number of records per batch request (requires batch mode).</summary>
    int BatchSize,

    /// <summary>Skip duplicate detection — always create records, never update existing ones.</summary>
    bool OverrideSafetyChecks,

    /// <summary>Maximum number of records to preload into cache per entity for duplicate detection.</summary>
    int PrefetchLimit,

    /// <summary>Delete existing records before importing new data.</summary>
    bool DeleteBeforeImport,

    /// <summary>Enable verbose CMT trace output.</summary>
    bool Verbose);
