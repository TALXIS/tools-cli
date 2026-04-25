namespace TALXIS.CLI.Platform.Xrm;

/// <summary>
/// Identity-neutral request record for <see cref="CmtExportRunner"/>. Mirrors
/// <see cref="CmtImportRequest"/> — connection details are passed separately.
/// </summary>
public sealed record CmtExportRequest(
    /// <summary>Path to the CMT schema file (data_schema.xml).</summary>
    string SchemaPath,

    /// <summary>Path for the output data package (.zip file).</summary>
    string OutputPath,

    /// <summary>Include binary file and image columns in the export.</summary>
    bool ExportFiles,

    /// <summary>Enable verbose CMT trace output.</summary>
    bool Verbose);
