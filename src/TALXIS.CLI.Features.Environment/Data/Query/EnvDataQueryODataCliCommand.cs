using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Config.Abstractions;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Query;

/// <summary>
/// Executes an OData query against the Dataverse environment via
/// <c>ServiceClient.ExecuteWebRequest</c>.
/// </summary>
/// <example>
///   txc environment data query odata accounts --select "name,accountnumber" --filter "statecode eq 0" --top 10
///   txc env data query odata contacts --select "fullname,emailaddress1" --json
/// </example>
[CliCommand(
    Name = "odata",
    Description = "Execute an OData query against the Dataverse environment."
)]
public class EnvDataQueryODataCliCommand : ProfiledCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(EnvDataQueryODataCliCommand));

    [CliArgument(Description = "The entity set name or OData path to query.")]
    public string Entity { get; set; } = string.Empty;

    [CliOption(Name = "--select", Description = "Comma-separated list of columns to return ($select).", Required = false)]
    public string? Select { get; set; }

    [CliOption(Name = "--filter", Description = "OData $filter expression.", Required = false)]
    public string? Filter { get; set; }

    [CliOption(Name = "--order-by", Description = "OData $orderby expression.", Required = false)]
    public string? OrderBy { get; set; }

    [CliOption(Name = "--top", Description = "Maximum number of records to return ($top).", Required = false)]
    public int? Top { get; set; }

    [CliOption(Name = "--include-annotations", Description = "Include OData annotations in the output.", Required = false)]
    public bool IncludeAnnotations { get; set; }

    [CliOption(Name = "--json", Description = "Emit the result as indented JSON instead of a text table.", Required = false)]
    public bool Json { get; set; }

    public async Task<int> RunAsync()
    {
        DataverseQueryResult result;
        try
        {
            var service = TxcServices.Get<IDataverseQueryService>();
            result = await service.QueryODataAsync(
                    Profile, Entity, Select, Filter, OrderBy, Top, IncludeAnnotations, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ConfigurationResolutionException or InvalidOperationException or NotSupportedException)
        {
            _logger.LogError("{Error}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "environment data query odata failed");
            return 1;
        }

        EnvDataQuerySqlCliCommand.OutputQueryResult(result, Json);
        return 0;
    }
}
