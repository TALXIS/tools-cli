using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Environment.Entity;
using TALXIS.CLI.Tests.Config.Commands;
using Xunit;

namespace TALXIS.CLI.Tests.Shared;

/// <summary>
/// A staged command run without --apply or --stage says that nothing ran, on stdout,
/// before it resolves a profile (no profile is configured in these tests).
/// </summary>
[Collection("TxcServicesSerial")]
public sealed class StagedCliCommandNotAppliedTests
{
    private static async Task<(int ExitCode, string Stdout)> RunAsync(TxcLeafCommand command)
    {
        var stdout = new StringWriter();
        int exitCode;
        using (OutputWriter.RedirectTo(stdout))
        {
            exitCode = await command.RunAsync();
        }

        return (exitCode, stdout.ToString());
    }

    [Fact]
    public async Task DeleteWithoutApply_Json_ReportsNothingDeleted()
    {
        using var host = new CommandTestHost();

        var (exitCode, stdout) = await RunAsync(new EntityDeleteCliCommand { Entity = "tom_project", Format = "json", Yes = true });

        Assert.Equal(2, exitCode);
        using var json = JsonDocument.Parse(stdout);
        Assert.Equal("not-applied", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("exitCode").GetInt32());
        Assert.StartsWith("Nothing deleted. Re-run with --apply", json.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task DeleteWithoutApply_Text_ReportsNothingDeleted()
    {
        using var host = new CommandTestHost();

        var (exitCode, stdout) = await RunAsync(new EntityDeleteCliCommand { Entity = "tom_project", Format = "text", Yes = true });

        Assert.Equal(2, exitCode);
        Assert.StartsWith("Nothing deleted. Re-run with --apply", stdout);
    }

    [Fact]
    public async Task NonDestructiveWithoutApply_ReportsNothingChanged()
    {
        using var host = new CommandTestHost();

        var (exitCode, stdout) = await RunAsync(new EntityCreateCliCommand { Name = "tom_project", Format = "text" });

        Assert.Equal(2, exitCode);
        Assert.StartsWith("Nothing changed. Re-run with --apply", stdout);
    }

    [Fact]
    public async Task ApplyAndStage_Json_ReportsFailedResult()
    {
        using var host = new CommandTestHost();

        var (exitCode, stdout) = await RunAsync(new EntityDeleteCliCommand { Entity = "tom_project", Format = "json", Apply = true, Stage = true });

        Assert.Equal(2, exitCode);
        using var json = JsonDocument.Parse(stdout);
        Assert.Equal("failed", json.RootElement.GetProperty("status").GetString());
        Assert.Contains("--apply and --stage", json.RootElement.GetProperty("message").GetString());
    }
}
