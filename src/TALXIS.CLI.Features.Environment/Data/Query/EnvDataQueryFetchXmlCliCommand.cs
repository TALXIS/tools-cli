using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Query;

/// <summary>
/// Executes a FetchXML query against the Dataverse environment via
/// <c>RetrieveMultiple</c>. The FetchXML can be passed inline or read
/// from a file with <c>--file</c>.
/// </summary>
/// <example>
///   txc environment data query fetchxml "&lt;fetch&gt;&lt;entity name='account'&gt;&lt;attribute name='name'/&gt;&lt;/entity&gt;&lt;/fetch&gt;"
///   txc env data query fetchxml --file ./query.xml --format json
/// </example>
[CliCommand(
    Name = "fetchxml",
    Description = "Execute a FetchXML query against the Dataverse environment."
)]
public class EnvDataQueryFetchXmlCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvDataQueryFetchXmlCliCommand));

    [CliArgument(Description = "The FetchXML query string (omit if using --file).", Required = false)]
    public string? FetchXml { get; set; }

    [CliOption(Name = "--file", Description = "Path to a file containing the FetchXML query.", Required = false)]
    public string? File { get; set; }

    [CliOption(Name = "--top", Description = "Maximum number of records to return.", Required = false)]
    public int? Top { get; set; }

    [CliOption(Name = "--include-annotations", Description = "Include OData annotations / formatted values in the output.", Required = false)]
    public bool IncludeAnnotations { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var fetchXml = ResolveFetchXml();
        if (fetchXml is null)
        {
            Logger.LogError("Provide a FetchXML string as an argument or use --file to specify a file.");
            return ExitValidationError;
        }

        var service = TxcServices.Get<IDataverseQueryService>();
        var result = await service.QueryFetchXmlAsync(Profile, fetchXml, Top, IncludeAnnotations, CancellationToken.None)
            .ConfigureAwait(false);

        EnvDataQuerySqlCliCommand.OutputQueryResult(result);
        return ExitSuccess;
    }

    /// <summary>
    /// Resolves the FetchXML from either the inline argument or the --file
    /// option. Returns <c>null</c> when neither is provided.
    /// </summary>
    private string? ResolveFetchXml()
    {
        if (!string.IsNullOrWhiteSpace(FetchXml))
            return FetchXml;

        if (!string.IsNullOrWhiteSpace(File))
        {
            if (!System.IO.File.Exists(File))
            {
                Logger.LogError("FetchXML file not found: {Path}", File);
                return null;
            }
            return System.IO.File.ReadAllText(File);
        }

        return null;
    }
}
