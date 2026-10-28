using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Features.Environment.Connection;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement.Commands;

[Collection("TxcServicesSerial")]
public sealed class ConnectionCliCommandTests
{
    private static ConnectionCreateCliCommand Create() => new()
    {
        Connector = "shared_commondataserviceforapps",
        DisplayName = "Dataverse",
    };

    [Fact]
    public async Task Create_ReadsSecretsFromTheNamedVariableAndNeverPrintsThem()
    {
        using var host = new ConnectionCommandTestHost();
        host.Environment["DEPLOY_SECRET"] = "s3cret";
        var command = Create();
        command.Param = ["token:clientId=abc"];
        command.SecretFromEnv = ["token:clientSecret=DEPLOY_SECRET"];

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(command);

        Assert.Equal(0, exit);
        var options = host.Connections.CreatedWith!;
        Assert.Equal("s3cret", options.Parameters["token:clientSecret"]);
        Assert.Equal("abc", options.Parameters["token:clientId"]);
        Assert.True(options.AttemptConsent);
        Assert.DoesNotContain("s3cret", output);
        Assert.Contains("\"status\": \"created\"", output);
        Assert.Contains("\"connectionId\": \"c1\"", output);
    }

    [Fact]
    public async Task Create_NoConsent_DoesNotAttemptConsent()
    {
        using var host = new ConnectionCommandTestHost();
        var command = Create();
        command.NoConsent = true;

        await ConnectionCommandTestHost.RunAsync(command);

        Assert.False(host.Connections.CreatedWith!.AttemptConsent);
    }

    [Fact]
    public async Task Create_ConsentRequired_ExitsOneButStillWritesThePayload()
    {
        using var host = new ConnectionCommandTestHost();
        host.Connections.CreateOutcome = new ConnectionCreateOutcome(
            ConnectionOutcomeStatus.ConsentRequired, "c1", "shared_office365users", "Mail", "Error",
            "https://consent.example/login", "This connector needs a person to sign in.");

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(Create());

        Assert.Equal(1, exit);
        Assert.Contains("\"status\": \"consent-required\"", output);
        Assert.Contains("\"consentLink\": \"https://consent.example/login\"", output);
        Assert.Contains("\"statusMessage\": \"This connector needs a person to sign in.\"", output);
    }

    [Fact]
    public async Task Create_Failed_ExitsOneWithThePayload()
    {
        using var host = new ConnectionCommandTestHost();
        host.Connections.CreateOutcome = new ConnectionCreateOutcome(
            ConnectionOutcomeStatus.Failed, "c1", "shared_x", "X", "Error", null, "Bad secret.");

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(Create());

        Assert.Equal(1, exit);
        Assert.Contains("\"status\": \"failed\"", output);
    }

    [Theory]
    [InlineData("novalue", null)]
    [InlineData(null, "token:clientSecret=NOT_SET_ANYWHERE")]
    public async Task Create_InvalidInput_ExitsTwoWithoutCallingTheService(string? param, string? secret)
    {
        using var host = new ConnectionCommandTestHost();
        var command = Create();
        command.Param = param is null ? null : [param];
        command.SecretFromEnv = secret is null ? null : [secret];

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(command);

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, output);
        Assert.Null(host.Connections.CreatedWith);
    }

    [Fact]
    public async Task Create_AnUnknownConnector_IsAValidationError()
    {
        using var host = new ConnectionCommandTestHost();
        host.Connections.Failure = new ConnectorNotFoundException("nope");

        var (exit, _) = await ConnectionCommandTestHost.RunAsync(Create());

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task List_PassesTheOptionalConnectorAndFilter()
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(
            new ConnectionListCliCommand { Connector = "shared_office365users", Filter = "mail" });

        Assert.Equal(0, exit);
        Assert.Equal("shared_office365users", host.Connections.LastConnector);
        Assert.Equal("mail", host.Connections.LastFilter);
        Assert.Contains("\"displayName\": \"Mail\"", output);
    }

    [Fact]
    public async Task Get_WithoutAConnector_LooksTheConnectionUpById()
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectionGetCliCommand { ConnectionId = "c1" });

        Assert.Equal(0, exit);
        Assert.Null(host.Connections.LastConnector);
        Assert.Contains("\"connectionId\": \"c1\"", output);
    }

    [Fact]
    public async Task Get_WithAConnector_PassesItAsADisambiguator()
    {
        using var host = new ConnectionCommandTestHost();

        await ConnectionCommandTestHost.RunAsync(new ConnectionGetCliCommand { ConnectionId = "c1", Connector = "shared_office365users" });

        Assert.Equal("shared_office365users", host.Connections.LastConnector);
    }

    [Theory]
    [MemberData(nameof(NotFoundErrors))]
    public async Task Get_ANotFoundOrAmbiguousConnection_ExitsTwo(Exception failure)
    {
        using var host = new ConnectionCommandTestHost();
        host.Connections.Failure = failure;

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectionGetCliCommand { ConnectionId = "c1" });

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, output);
    }

    public static TheoryData<Exception> NotFoundErrors() => new()
    {
        new ConnectionNotFoundException("c1"),
        new ConnectionAccessDeniedException("c1", "shared_office365users"),
        new AmbiguousConnectionException("c1", ["shared_a", "shared_b"]),
    };

    [Fact]
    public async Task Test_Healthy_ReportsStatusAlongsideTheConnectionStatus()
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectionTestCliCommand { ConnectionId = "c1" });

        Assert.Equal(0, exit);
        Assert.Contains("\"status\": \"healthy\"", output);
        Assert.Contains("\"connectionStatus\": \"Connected\"", output);
    }

    [Fact]
    public async Task Test_Unhealthy_ExitsOneWithThePayload()
    {
        using var host = new ConnectionCommandTestHost();
        host.Connections.TestOutcome = new ConnectionTestOutcome(false, "Error", "Token expired.");

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectionTestCliCommand { ConnectionId = "c1" });

        Assert.Equal(1, exit);
        Assert.Contains("\"status\": \"unhealthy\"", output);
        Assert.Contains("\"statusMessage\": \"Token expired.\"", output);
    }

    [Fact]
    public async Task Test_NotRunnableForTheCredential_IsUntestedAndSucceeds()
    {
        using var host = new ConnectionCommandTestHost();
        host.Connections.TestOutcome = new ConnectionTestOutcome(true, "Connected", "Only for a user credential.", Tested: false);

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectionTestCliCommand { ConnectionId = "c1" });

        Assert.Equal(0, exit);
        Assert.Contains("\"status\": \"untested\"", output);
        Assert.Contains("\"connectionStatus\": \"Connected\"", output);
    }

    [Fact]
    public async Task Delete_WithoutYes_RefusesInANonInteractiveRun()
    {
        using var host = new ConnectionCommandTestHost(headless: true);

        var (exit, _) = await ConnectionCommandTestHost.RunAsync(new ConnectionDeleteCliCommand { ConnectionId = "c1" });

        Assert.Equal(2, exit);
        Assert.False(host.Connections.Deleted);
    }

    [Fact]
    public async Task Delete_WithYes_UsesTheDeletedStatusAndAnOptionalConnector()
    {
        using var host = new ConnectionCommandTestHost(headless: true);

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectionDeleteCliCommand { ConnectionId = "c1", Yes = true });

        Assert.Equal(0, exit);
        Assert.True(host.Connections.Deleted);
        Assert.Null(host.Connections.LastConnector);
        Assert.Contains($"\"status\": \"{ConnectionOutcomeStatus.Deleted.ToKebabCase()}\"", output);
    }
}
