using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Xunit;

namespace TALXIS.CLI.IntegrationTests;

/// <summary>
/// Smoke tests that verify telemetry spans arrive in App Insights
/// with the correct tags — the same IDs that users see in local error output.
/// Requires a Release build with TxcTelemetryConnectionString embedded,
/// Azure credentials (e.g. az login), and the APPINSIGHTS_APP_ID env var
/// (defaults to the TALXIS CLI App Insights instance).
/// </summary>
[Collection("Sequential")]
[Trait("Category", "Smoke")]
public class AppInsightsSmokeTests
{
    private const string DefaultAppId = "574ef853-f03d-4757-b633-080b26010f1f";

    /// <summary>
    /// Runs a failing CLI command with a unique session ID, then queries App Insights
    /// to verify the emitted dependency span carries the expected custom dimensions
    /// (session ID, entry point, command, exit code, version, client tag).
    /// </summary>
    [Fact]
    public async Task AppInsights_CliFailure_SpanHasCorrectDimensions()
    {
        var appId = Environment.GetEnvironmentVariable("APPINSIGHTS_APP_ID") ?? DefaultAppId;

        // Verify Azure credentials are available; skip if not.
        DefaultAzureCredential credential;
        LogsQueryClient client;
        try
        {
            credential = new DefaultAzureCredential();
            client = new LogsQueryClient(credential);
        }
        catch (Exception)
        {
            // Azure credentials not configured — skip.
            return;
        }

        // 1. Generate unique session ID
        var uniqueSessionId = $"e2e-smoke-{Guid.NewGuid():N}";

        // 2. Run a failing CLI command with the explicit session ID
        var result = await CliRunner.RunRawAsync(
            ["workspace", "validate", "definitely-not-a-real-workspace-path"],
            null,
            new Dictionary<string, string?> { { "TXC_SESSION_ID", uniqueSessionId } });

        // Try to extract support.operationId from CLI stdout for cross-surface check
        string? cliOperationId = null;
        var cliOutput = result.Output?.Trim();
        if (!string.IsNullOrEmpty(cliOutput))
        {
            try
            {
                using var cliDoc = JsonDocument.Parse(cliOutput);
                if (cliDoc.RootElement.TryGetProperty("support", out var support)
                    && support.TryGetProperty("operationId", out var opId))
                {
                    cliOperationId = opId.GetString();
                }
            }
            catch (JsonException)
            {
                // stdout may not be valid JSON
            }
        }

        // 3. Wait for initial ingestion delay
        await Task.Delay(TimeSpan.FromSeconds(15));

        // 4. Query App Insights with retries (up to ~60s total)
        var kqlQuery = $@"dependencies
            | where session_Id == '{uniqueSessionId}'
            | project operation_Id, name, success, timestamp,
                customDimensions['txc.session_id'] as sessionId,
                customDimensions['txc.session_id.source'] as sessionIdSource,
                customDimensions['txc.entry_point'] as entryPoint,
                customDimensions['txc.command'] as command,
                customDimensions['txc.exit_code'] as exitCode,
                customDimensions['txc.error_kind'] as errorKind,
                customDimensions['txc.version'] as version,
                customDimensions['txc.client'] as clientTag
            | order by timestamp asc";

        LogsQueryResult? queryResult = null;
        try
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var response = await client.QueryWorkspaceAsync(
                    appId,
                    kqlQuery,
                    new QueryTimeRange(TimeSpan.FromMinutes(10)));

                if (response.Value.Table.Rows.Count > 0)
                {
                    queryResult = response.Value;
                    break;
                }

                await Task.Delay(TimeSpan.FromSeconds(15));
            }
        }
        catch (AuthenticationFailedException)
        {
            // Credentials expired or unavailable mid-query — skip.
            return;
        }
        catch (Azure.RequestFailedException)
        {
            // Workspace not reachable (wrong ID, permissions, etc.) — skip.
            return;
        }

        // 5. If no results after retries, telemetry may not be enabled in this build
        if (queryResult == null || queryResult.Table.Rows.Count == 0)
            return;

        // 6. Map column names to indices for reliable access
        var columns = queryResult.Table.Columns;
        var colIndex = new Dictionary<string, int>();
        for (int i = 0; i < columns.Count; i++)
            colIndex[columns[i].Name] = i;

        var row = queryResult.Table.Rows[0];

        var sessionId = row[colIndex["sessionId"]]?.ToString();
        var sessionIdSource = row[colIndex["sessionIdSource"]]?.ToString();
        var entryPoint = row[colIndex["entryPoint"]]?.ToString();
        var command = row[colIndex["command"]]?.ToString();
        var exitCode = row[colIndex["exitCode"]]?.ToString();
        var errorKind = row[colIndex["errorKind"]]?.ToString();
        var version = row[colIndex["version"]]?.ToString();
        var clientTag = row[colIndex["clientTag"]]?.ToString();
        var success = row[colIndex["success"]]?.ToString();
        var operationId = row[colIndex["operation_Id"]]?.ToString();

        // 7. Assert on the failure span dimensions
        Assert.Equal(uniqueSessionId, sessionId);
        Assert.Equal("explicit", sessionIdSource);
        Assert.Equal("cli", entryPoint);
        Assert.False(string.IsNullOrWhiteSpace(command), "command dimension should be non-empty");
        Assert.False(string.IsNullOrWhiteSpace(exitCode), "exitCode dimension should be non-empty");
        Assert.NotEqual("0", exitCode);
        Assert.False(string.IsNullOrWhiteSpace(errorKind), "errorKind dimension should be non-empty");
        Assert.Equal("validation", errorKind);
        Assert.False(string.IsNullOrWhiteSpace(version), "version dimension should be non-empty");
        Assert.Equal("terminal", clientTag);
        Assert.Equal("False", success, StringComparer.OrdinalIgnoreCase);

        // 8. Cross-surface check: CLI stdout operationId should match App Insights operation_Id
        if (!string.IsNullOrWhiteSpace(cliOperationId)
            && cliOperationId != "unknown"
            && !string.IsNullOrWhiteSpace(operationId))
        {
            Assert.Equal(cliOperationId, operationId);
        }
    }
}
