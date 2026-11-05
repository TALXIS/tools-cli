namespace TALXIS.CLI.Core.Contracts.Dataverse;

/// <summary>
/// A query reached Dataverse and failed there (unknown table, missing
/// privilege, malformed query). Failures to connect or authenticate are not
/// wrapped in it.
/// </summary>
public sealed class DataverseQueryException : InvalidOperationException
{
    public DataverseQueryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
