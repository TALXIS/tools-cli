using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Environment.Data.Query;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Data;

[Collection("TxcServicesSerial")]
public sealed class EnvDataQueryODataCliCommandTests
{
    private static readonly Exception NotImplementedFault = new InvalidOperationException(
        "OData query against 'EntityDefinitions' failed: NotImplemented",
        new HttpRequestException("NotImplemented"));

    [Fact]
    public async Task RunAsync_EntityDefinitionsFails_PointsToEntityDescribe()
    {
        var (exit, output) = await RunAsync("EntityDefinitions", NotImplementedFault);

        Assert.Equal(1, exit);
        var message = JsonDocument.Parse(output).RootElement.GetProperty("message").GetString();
        Assert.Contains("NotImplemented", message);
        Assert.Contains("txc env entity describe", message);
    }

    [Fact]
    public async Task RunAsync_RecordQueryFails_HasNoMetadataHint()
    {
        var (exit, output) = await RunAsync("accounts", NotImplementedFault);

        Assert.Equal(1, exit);
        Assert.DoesNotContain("entity describe", output);
    }

    [Theory]
    [InlineData("EntityDefinitions", true)]
    [InlineData("/entitydefinitions", true)]
    [InlineData("EntityDefinitions(LogicalName='account')/Attributes", true)]
    [InlineData("EntityDefinitions?$select=LogicalName", true)]
    [InlineData("accounts", false)]
    [InlineData("EntityDefinitionsX", false)]
    [InlineData("", false)]
    public void IsEntityDefinitionsPath_RecognizesMetadataPaths(string path, bool expected)
    {
        Assert.Equal(expected, ODataMetadataPathHint.IsEntityDefinitionsPath(path));
    }

    private static async Task<(int Exit, string Output)> RunAsync(string entity, Exception failure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataverseQueryService>(new FailingQueryService(failure));
        using var provider = services.BuildServiceProvider();
        TxcServices.Initialize(provider);
        try
        {
            var output = new StringWriter();
            int exit;
            using (OutputWriter.RedirectTo(output))
            {
                exit = await new EnvDataQueryODataCliCommand { Format = "json", Entity = entity }.RunAsync();
            }

            return (exit, output.ToString());
        }
        finally
        {
            TxcServices.Reset();
        }
    }

    private sealed class FailingQueryService(Exception failure) : IDataverseQueryService
    {
        public Task<DataverseQueryResult> QueryODataAsync(string? profileName, string entitySetOrPath, string? select, string? filter, string? orderBy, int? top, bool includeAnnotations, CancellationToken ct)
            => Task.FromException<DataverseQueryResult>(failure);

        public Task<DataverseQueryResult> QuerySqlAsync(string? profileName, string sql, int? top, bool includeAnnotations, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<DataverseQueryResult> QueryFetchXmlAsync(string? profileName, string fetchXml, int? top, bool includeAnnotations, CancellationToken ct)
            => throw new NotImplementedException();
    }
}
