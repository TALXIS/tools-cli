using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

/// <summary>
/// The guide must attach real template parameters to scaffolding recipes.
/// These cover the pure pieces — extracting the template short-names from a recipe and
/// rendering the authoritative parameter block — plus the DTO round-trip that the
/// subprocess provider relies on.
/// </summary>
public class TemplateParameterEnricherTests
{
    [Fact]
    public void ExtractTemplateShortNames_PrefersExplicitTemplatesLine()
    {
        var recipe = """
            1. Create the entity
            2. Add an attribute

            TEMPLATES: pp-entity, pp-entity-attribute
            """;

        var names = TemplateParameterEnricher.ExtractTemplateShortNames(recipe);

        Assert.Equal(new[] { "pp-entity", "pp-entity-attribute" }, names);
    }

    [Fact]
    public void ExtractTemplateShortNames_FallsBackToScanningPpTokens()
    {
        var recipe = "Run execute_operation workspace_component_create with type=pp-entity, then add a pp-api-endpoint.";

        var names = TemplateParameterEnricher.ExtractTemplateShortNames(recipe);

        Assert.Equal(new[] { "pp-entity", "pp-api-endpoint" }, names);
    }

    [Fact]
    public void ExtractTemplateShortNames_DeduplicatesCaseInsensitively()
    {
        var recipe = "TEMPLATES: pp-entity, PP-Entity, pp-entity";

        var names = TemplateParameterEnricher.ExtractTemplateShortNames(recipe);

        Assert.Single(names);
        Assert.Equal("pp-entity", names[0]);
    }

    [Fact]
    public void ExtractTemplateShortNames_EmptyOrNull_ReturnsEmpty()
    {
        Assert.Empty(TemplateParameterEnricher.ExtractTemplateShortNames(null));
        Assert.Empty(TemplateParameterEnricher.ExtractTemplateShortNames("just prose, no templates here"));
    }

    [Fact]
    public void BuildAuthoritativeBlock_GroupsRequiredOptional_AndRendersChoicesAndConditions()
    {
        var parameters = new List<TemplateParameterInfo>
        {
            new() { Name = "AttributeType", DataType = "choice", Required = true, Choices = "Text, Lookup, Decimal", Description = "Data type" },
            new() { Name = "MaxLength", DataType = "int", Required = false, DefaultValue = "100", AppliesWhen = "AttributeType == \"Text\"" },
            new() { Name = "ReferencedEntityName", DataType = "text", Required = false, RequiredWhen = "AttributeType == \"Lookup\"" },
        };

        var block = TemplateParameterEnricher.BuildAuthoritativeBlock(
            new[] { ("pp-entity-attribute", (IReadOnlyList<TemplateParameterInfo>)parameters) });

        // Header + template heading present.
        Assert.Contains("authoritative", block, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("### pp-entity-attribute", block);
        // Required/optional split.
        Assert.Contains("Required:", block);
        Assert.Contains("Optional:", block);
        // Real names, choices, conditions surfaced.
        Assert.Contains("`AttributeType`", block);
        Assert.Contains("choices: Text, Lookup, Decimal", block);
        Assert.Contains("applies when: AttributeType == \"Text\"", block);
        Assert.Contains("required when: AttributeType == \"Lookup\"", block);
    }

    private sealed class FakeProvider : ITemplateParameterProvider
    {
        private readonly Dictionary<string, IReadOnlyList<TemplateParameterInfo>?> _map;
        public int Calls { get; private set; }
        public FakeProvider(Dictionary<string, IReadOnlyList<TemplateParameterInfo>?> map) => _map = map;
        public Task<IReadOnlyList<TemplateParameterInfo>?> GetParametersAsync(string templateShortName, CancellationToken ct)
        {
            Calls++;
            _map.TryGetValue(templateShortName, out var v);
            return Task.FromResult(v);
        }
    }

    private static readonly IReadOnlyList<TemplateParameterInfo> EntityParams = new[]
    {
        new TemplateParameterInfo { Name = "EntitySchemaName", DataType = "text", Required = true },
        new TemplateParameterInfo { Name = "PublisherPrefix", DataType = "text", Required = false },
    };

    [Fact]
    public async Task EnrichAsync_AppendsAuthoritativeBlock_WhenScaffoldingAndTemplateResolves()
    {
        var provider = new FakeProvider(new() { ["pp-entity"] = EntityParams });
        var recipe = "1. scaffold\n\nTEMPLATES: pp-entity";

        var result = await TemplateParameterEnricher.EnrichAsync(
            recipe, new[] { "workspace_component_create" }, provider, CancellationToken.None);

        Assert.Contains("Exact template parameters", result);
        Assert.Contains("`EntitySchemaName`", result);
        Assert.StartsWith(recipe.TrimEnd(), result); // original recipe preserved, block appended
    }

    [Fact]
    public async Task EnrichAsync_NoOp_WhenComponentCreateNotMatched()
    {
        var provider = new FakeProvider(new() { ["pp-entity"] = EntityParams });
        var recipe = "1. do something\n\nTEMPLATES: pp-entity";

        var result = await TemplateParameterEnricher.EnrichAsync(
            recipe, new[] { "environment_solution_export" }, provider, CancellationToken.None);

        Assert.Equal(recipe, result);
        Assert.Equal(0, provider.Calls); // provider not even consulted
    }

    [Fact]
    public async Task EnrichAsync_NoOp_WhenProviderNullOrTemplateUnknown()
    {
        var recipe = "TEMPLATES: pp-entity";
        // null provider
        Assert.Equal(recipe, await TemplateParameterEnricher.EnrichAsync(
            recipe, new[] { "workspace_component_create" }, null, CancellationToken.None));
        // provider returns null for the template
        var provider = new FakeProvider(new() { ["pp-entity"] = null });
        Assert.Equal(recipe, await TemplateParameterEnricher.EnrichAsync(
            recipe, new[] { "workspace_component_create" }, provider, CancellationToken.None));
    }

    [Fact]
    public void TemplateParameterInfo_RoundTrips_FromParameterListJsonShape()
    {
        // Mirrors the shape emitted by `workspace component parameter list --format json`.
        var payload = new[]
        {
            new
            {
                name = "ReferencedEntityName",
                displayName = "",
                dataType = "text",
                required = false,
                choices = (string?)null,
                appliesWhen = (string?)null,
                requiredWhen = "AttributeType == \"Lookup\"",
                description = "Logical name of the referenced entity",
            }
        };
        var json = JsonSerializer.Serialize(payload, TxcOutputJsonOptions.Default);

        var parsed = JsonSerializer.Deserialize<List<TemplateParameterInfo>>(json, TxcOutputJsonOptions.Default);

        var p = Assert.Single(parsed!);
        Assert.Equal("ReferencedEntityName", p.Name);
        Assert.Equal("text", p.DataType);
        Assert.False(p.Required);
        Assert.Equal("AttributeType == \"Lookup\"", p.RequiredWhen);
    }
}
