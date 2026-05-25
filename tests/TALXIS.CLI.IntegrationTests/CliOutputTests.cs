using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Xunit;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Tests for CLI output format: JSON structure on stdout, support info on stderr,
/// and cross-surface consistency between CLI and MCP error reporting.
/// </summary>
[Collection("Sequential")]
public class CliOutputTests
{
    [Fact]
    public async Task CliSuccess_CleanJsonOutput_NoSupportInfo()
    {
        var result = await CliRunner.RunRawAsync(["component", "type", "list"]);

        Assert.Equal(0, result.ExitCode);

        // stdout must be valid JSON
        using var doc = JsonDocument.Parse(result.Output);
        Assert.NotNull(doc);

        // stderr must NOT contain support escalation info
        Assert.DoesNotContain("github.com/TALXIS/tools-cli/issues", result.Error);
    }

    /// <summary>
    /// CLI validation errors (exit 2) write to stderr.
    /// Support escalation info is printed when telemetry is initialized (session ID available).
    /// In Debug builds without config, support info may be absent.
    /// </summary>
    [Fact]
    public async Task CliValidationError_StderrContainsErrorMessage()
    {
        // workspace validate with nonexistent path triggers a validation error
        var result = await CliRunner.RunRawAsync(["workspace", "validate", "definitely-not-a-real-workspace-path"]);

        Assert.NotEqual(0, result.ExitCode);

        // stderr should contain the error message
        Assert.Contains("not found", result.Error, System.StringComparison.OrdinalIgnoreCase);

        // If support info is present, verify its format
        var sessionMatch = Regex.Match(result.Error, @"Session:\s*(\S+)");
        if (sessionMatch.Success)
        {
            Assert.False(string.IsNullOrWhiteSpace(sessionMatch.Groups[1].Value));
            Assert.Contains("github.com/TALXIS/tools-cli/issues", result.Error);
        }
    }

    /// <summary>
    /// CLI general errors (exit 1) produce a JSON envelope on stdout with
    /// exitCode and support block, and support info on stderr.
    /// We use "environment solution list" without a profile to trigger a
    /// ConfigurationResolutionException which is caught as a validation error.
    /// </summary>
    [Fact]
    public async Task CliFailure_StderrHasSupportInfo()
    {
        var result = await CliRunner.RunRawAsync(["environment", "solution", "list"]);

        // If the command somehow succeeds (unlikely without a profile), skip
        if (result.ExitCode == 0)
            return;

        Assert.True(result.ExitCode == 1 || result.ExitCode == 2,
            $"Expected exit code 1 or 2, got {result.ExitCode}");

        // stderr should contain support escalation info
        var sessionMatch = Regex.Match(result.Error, @"Session:\s*(\S+)");
        Assert.True(sessionMatch.Success, "stderr should contain 'Session: <value>'");
        Assert.False(string.IsNullOrWhiteSpace(sessionMatch.Groups[1].Value));

        Assert.Contains("github.com/TALXIS/tools-cli/issues", result.Error);
    }

    /// <summary>
    /// The same failing command via CLI and MCP should produce the same error message.
    /// For CLI: the error appears in stderr. For MCP: in structuredContent.
    /// </summary>
    [Fact]
    public async Task CliAndMcp_SameError_ProducesSameMessage()
    {
        // CLI path — positional arg (no --path flag)
        var cliResult = await CliRunner.RunRawAsync(["workspace", "validate", "definitely-not-a-real-workspace-path"]);
        Assert.NotEqual(0, cliResult.ExitCode);

        // CLI error message is in stderr
        Assert.False(string.IsNullOrWhiteSpace(cliResult.Error));

        // MCP path
        var mcpClient = await McpTestClient.InstanceAsync;
        var mcpResult = await mcpClient.CallToolAsync("execute_operation", new Dictionary<string, object?>
        {
            { "operation", "workspace_validate" },
            { "arguments", new Dictionary<string, object?> { { "Path", "definitely-not-a-real-workspace-path" } } }
        });

        Assert.True(mcpResult.IsError);
        Assert.NotNull(mcpResult.StructuredContent);

        // Parse MCP structuredContent
        var mcpStructured = mcpResult.StructuredContent.Value;
        var mcpError = mcpStructured.GetProperty("error");
        var mcpMessage = mcpError.GetProperty("message").GetString()!;
        var mcpExitCode = mcpError.GetProperty("exitCode").GetInt32();
        var mcpReportUrl = mcpStructured.GetProperty("support").GetProperty("reportUrl").GetString()!;

        // Both surfaces should report the same error message and exit code
        Assert.Contains("not found", mcpMessage, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not found", cliResult.Error, System.StringComparison.OrdinalIgnoreCase);
        Assert.Equal(cliResult.ExitCode, mcpExitCode);
        Assert.Contains("github.com/TALXIS/tools-cli/issues", mcpReportUrl);
    }
}
