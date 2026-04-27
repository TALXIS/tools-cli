using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Data.Query;

/// <summary>
/// Executes an OData query against the Dataverse environment via
/// <c>ServiceClient.ExecuteWebRequest</c>.
/// </summary>
/// <example>
///   txc environment data query odata accounts --select "name,accountnumber" --filter "statecode eq 0" --top 10
///   txc env data query odata contacts --select "fullname,emailaddress1" --format json
/// </example>
[CliReadOnly]
[CliCommand(
    Name = "odata",
    Description = "Executes an OData query against the LIVE Dataverse environment. Requires an active profile. Preferred for simple, strongly-typed queries."
)]
public class EnvDataQueryODataCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvDataQueryODataCliCommand));

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

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IDataverseQueryService>();
        var result = await service.QueryODataAsync(
                Profile, Entity, Select, Filter, OrderBy, Top, IncludeAnnotations, CancellationToken.None)
            .ConfigureAwait(false);

        EnvDataQuerySqlCliCommand.OutputQueryResult(result);
        return ExitSuccess;
    }
}
