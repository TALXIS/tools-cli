using System.Text.Json;
using TALXIS.CLI.Features.Environment.Solution;
using TALXIS.CLI.MCP;
using Xunit;

namespace TALXIS.CLI.Tests.MCP;

/// <summary>
/// Verifies that the MCP tool input-schema surface automatically exposes
/// the <c>profile</c> argument on every Connection-touching tool — i.e.
/// every command that derives from <c>ProfiledCliCommand</c>. This is
/// the per-call override contract documented in
/// <c>src/TALXIS.CLI.MCP/README.md#per-call-profile-override</c>: an MCP
/// client may pass <c>{ "profile": "customer-a-dev" }</c> on a single
/// tool call, and <see cref="CliCommandAdapter.BuildCliArgs"/> forwards
/// it as <c>--profile=&lt;name&gt;</c>, enabling context switching
/// within one MCP session without restart.
///
/// The test reflects the live command type so that it cannot silently
/// stop working if a future refactor moves <c>Profile</c> off the base
/// class or drops the <c>[CliOption]</c> attribute.
/// </summary>
public class CliCommandAdapterProfileArgumentTests
{
    [Fact]
    public void InputSchema_ProfiledCommand_ExposesProfileArgument()
    {
        var adapter = new CliCommandAdapter();
        var schema = adapter.BuildInputSchema(typeof(SolutionListCliCommand));

        // Schema shape: { "type": "object", "properties": { ... }, "required": [...] }
        Assert.True(schema.TryGetProperty("properties", out var props));
        Assert.True(props.TryGetProperty("profile", out var profile),
            "Connection-touching tools must expose a 'profile' property so MCP clients can override per-call.");

        Assert.Equal("string", profile.GetProperty("type").GetString());
        // Profile is optional on ProfiledCliCommand; every derived command
        // inherits Required=false and must NOT appear in the required list.
        // JSON Schema allows omitting "required" entirely when there are no
        // required members, so treat an absent property as an empty set.
        var required = schema.TryGetProperty("required", out var requiredProperty)
            ? requiredProperty.EnumerateArray().Select(x => x.GetString()).ToList()
            : new List<string?>();
        Assert.DoesNotContain("profile", required);
    }

    [Fact]
    public void BuildCliArgs_ProfileArgument_ForwardedAsFlag()
    {
        var adapter = new CliCommandAdapter();
        var args = new Dictionary<string, JsonElement>
        {
            ["profile"] = JsonSerializer.SerializeToElement("customer-a-dev"),
        };

        var cliArgs = adapter.BuildCliArgs("environment_solution_list", args);
        Assert.Contains("--profile", cliArgs);
        Assert.Contains("customer-a-dev", cliArgs);
    }
}
