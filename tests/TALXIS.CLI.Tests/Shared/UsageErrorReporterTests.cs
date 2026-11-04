using System.Text.Json;
using DotMake.CommandLine;
using TALXIS.CLI.Core;
using Xunit;

namespace TALXIS.CLI.Tests.Shared;

[Collection("CliParserSerial")]
public sealed class UsageErrorReporterTests
{
    private static readonly string[] OldEntityDeleteShape =
        ["environment", "entity", "delete", "--entity", "tom_project", "--apply"];

    private static string[] With(params string[] extra) => OldEntityDeleteShape.Concat(extra).ToArray();

    [Theory]
    [InlineData("--format", "json")]
    [InlineData("-f", "json")]
    [InlineData("--format=json")]
    [InlineData("--format:JSON")]
    [InlineData("-f:json")]
    public void JsonFormat_WritesFailedResultOnStdout(params string[] format)
    {
        var args = With(format);
        var parsed = Cli.Parse<TALXIS.CLI.TxcCliCommand>(args);
        Assert.True(UsageErrorReporter.HasUsageErrors(parsed.ParseResult));

        var stdout = new StringWriter();
        bool written;
        using (OutputWriter.RedirectTo(stdout))
        {
            written = UsageErrorReporter.TryWriteJson(parsed.ParseResult, args);
        }

        Assert.True(written);
        using var json = JsonDocument.Parse(stdout.ToString());
        Assert.Equal("failed", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("exitCode").GetInt32());
        var message = json.RootElement.GetProperty("message").GetString();
        Assert.Contains("tom_project", message);
        Assert.Contains("txc environment entity delete --help", message);
    }

    [Fact]
    public void TextFormat_LeavesOutputToTheParser()
    {
        var args = With("--format", "text");
        var parsed = Cli.Parse<TALXIS.CLI.TxcCliCommand>(args);

        var stdout = new StringWriter();
        using (OutputWriter.RedirectTo(stdout))
        {
            Assert.False(UsageErrorReporter.TryWriteJson(parsed.ParseResult, args));
        }

        Assert.Equal(string.Empty, stdout.ToString());
    }

    [Fact]
    public void Help_IsNotAUsageError()
    {
        var parsed = Cli.Parse<TALXIS.CLI.TxcCliCommand>(new[] { "environment", "entity", "delete", "--help" });

        Assert.False(UsageErrorReporter.HasUsageErrors(parsed.ParseResult));
    }

    [Fact]
    public void ValidShape_IsNotAUsageError()
    {
        var parsed = Cli.Parse<TALXIS.CLI.TxcCliCommand>(new[] { "environment", "entity", "delete", "tom_project", "--format", "json" });

        Assert.False(UsageErrorReporter.HasUsageErrors(parsed.ParseResult));
    }

    [Theory]
    [InlineData(null, "environment", "entity", "list")]
    [InlineData(OutputFormat.Text, "environment", "-f", "text")]
    [InlineData(null, "environment", "--format", "yaml")]
    [InlineData(null, "environment", "--", "--format", "json")]
    public void ReadFormat_OnlyHonoursKnownExplicitValues(OutputFormat? expected, params string[] args)
    {
        Assert.Equal(expected, UsageErrorReporter.ReadFormat(args));
    }
}
