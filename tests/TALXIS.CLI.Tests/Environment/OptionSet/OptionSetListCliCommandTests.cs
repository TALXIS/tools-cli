using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Environment.OptionSet;
using TALXIS.CLI.Platform.Dataverse.Application.Services;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.OptionSet;

[Collection("TxcServicesSerial")]
public sealed class OptionSetListCliCommandTests
{
    private static readonly GlobalOptionSetSummaryRecord[] Rows =
    [
        new("tom_priority", "Priority", "Picklist", 4, true),
        new("tom_status", "Project Stage", "Picklist", 3, true),
        new("budgetstatus", null, "Picklist", 2, false),
    ];

    [Fact]
    public async Task RunAsync_Search_IsPassedToService()
    {
        var service = new FakeOptionSetService();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IDataverseOptionSetService>(service);
        using var provider = services.BuildServiceProvider();
        TxcServices.Initialize(provider);
        try
        {
            var output = new StringWriter();
            int exit;
            using (OutputWriter.RedirectTo(output))
            {
                exit = await new OptionSetListCliCommand { Format = "json", Search = "stage" }.RunAsync();
            }

            Assert.Equal(0, exit);
            Assert.Equal("stage", service.LastSearch);
            var names = JsonDocument.Parse(output.ToString()).RootElement.EnumerateArray()
                .Select(e => e.GetProperty("name").GetString())
                .ToArray();
            Assert.Equal(["tom_status"], names);
        }
        finally
        {
            TxcServices.Reset();
        }
    }

    [Theory]
    [InlineData("PRIORITY", new[] { "tom_priority" })]
    [InlineData("project stage", new[] { "tom_status" })]
    [InlineData("status", new[] { "tom_status", "budgetstatus" })]
    [InlineData("nothing", new string[0])]
    public void FilterBySearch_MatchesNameOrDisplayName(string search, string[] expected)
    {
        var names = DataverseOptionSetService.FilterBySearch(Rows, search).Select(r => r.Name).ToArray();

        Assert.Equal(expected, names);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void FilterBySearch_EmptySearch_ReturnsAllRows(string? search)
    {
        Assert.Equal(Rows.Length, DataverseOptionSetService.FilterBySearch(Rows, search).Count());
    }

    private sealed class FakeOptionSetService : IDataverseOptionSetService
    {
        public string? LastSearch { get; private set; }

        public Task<IReadOnlyList<GlobalOptionSetSummaryRecord>> ListGlobalOptionSetsAsync(string? profileName, string? search, CancellationToken ct)
        {
            LastSearch = search;
            return Task.FromResult<IReadOnlyList<GlobalOptionSetSummaryRecord>>(DataverseOptionSetService.FilterBySearch(Rows, search).ToList());
        }

        public Task CreateGlobalOptionSetAsync(string? profileName, string schemaName, string displayName, string? description, OptionMetadataInput[] options, string? solution, CancellationToken ct)
            => throw new NotImplementedException();

        public Task InsertOptionAsync(string? profileName, string? entityName, string? attributeName, string? optionSetName, string label, int? value, CancellationToken ct)
            => throw new NotImplementedException();

        public Task DeleteOptionAsync(string? profileName, string? entityName, string? attributeName, string? optionSetName, int value, CancellationToken ct)
            => throw new NotImplementedException();

        public Task DeleteGlobalOptionSetAsync(string? profileName, string optionSetName, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<GlobalOptionSetDetailRecord> DescribeGlobalOptionSetAsync(string? profileName, string optionSetName, int? languageCode, CancellationToken ct)
            => throw new NotImplementedException();
    }
}
