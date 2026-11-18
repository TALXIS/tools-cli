using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Identity;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Platform.Dataverse.Runtime;
using TALXIS.CLI.Tests.Config.Commands;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.Dataverse;

[Collection("TxcServicesSerial")]
public sealed class ManagedIdentityAccessTokenTests
{
    private static readonly Connection Connection = new()
    {
        Id = "dev",
        Provider = ProviderKind.Dataverse,
        EnvironmentUrl = "https://contoso.crm.dynamics.com/",
    };

    private static readonly Credential Credential = new()
    {
        Id = "managed-identity",
        Kind = CredentialKind.ManagedIdentity,
    };

    [Fact]
    public async Task MsiEndpoint_UsesDefaultIdentityAndResourceAudiences_WithPerResourceCaching()
    {
        await using var endpoint = new ManagedIdentityEndpoint();
        using var environment = new ManagedIdentityEnvironment(endpoint.Url);
        using var host = new CommandTestHost(headless: true);
        var tokens = CreateTokenService(host);

        var dataverse = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => tokens.AcquireAsync(Connection, Credential, default)));
        Assert.All(dataverse, token => Assert.Equal("token-1", token));
        Assert.Single(endpoint.Requests);

        Assert.Equal("token-2", await tokens.AcquireForResourceAsync(
            Connection, Credential, new Uri("https://graph.microsoft.com/"), default));
        Assert.Equal("token-3", await tokens.AcquireForResourceAsync(
            Connection, Credential, new Uri("https://service.powerapps.com/"), default));
        Assert.Equal("token-2", await tokens.AcquireForResourceAsync(
            Connection, Credential, new Uri("https://graph.microsoft.com/"), default));

        Assert.Equal(3, endpoint.Requests.Count);
        Assert.Equal(
            ["resource=https://contoso.crm.dynamics.com", "resource=https://graph.microsoft.com", "resource=https://service.powerapps.com"],
            endpoint.Requests.Select(r => Uri.UnescapeDataString(r.Body)).ToArray());
        Assert.All(endpoint.Requests, request =>
        {
            Assert.StartsWith("POST ", request.Headers);
            Assert.Contains("Metadata: true", request.Headers, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("client_id", request.Body);
            Assert.DoesNotContain("object_id", request.Body);
            Assert.DoesNotContain("msi_res_id", request.Body);
        });
        Assert.Equal(0, host.Login.Calls);
        Assert.Empty(host.Vault.Contents);
    }

    [Fact]
    public async Task MsiEndpoint_RefreshesTokensWithinTheRenewalBuffer()
    {
        await using var endpoint = new ManagedIdentityEndpoint { Lifetime = TimeSpan.FromSeconds(30) };
        using var environment = new ManagedIdentityEnvironment(endpoint.Url);
        using var host = new CommandTestHost(headless: true);
        var tokens = CreateTokenService(host);

        Assert.Equal("token-1", await tokens.AcquireAsync(Connection, Credential, default));
        Assert.Equal("token-2", await tokens.AcquireAsync(Connection, Credential, default));
        Assert.Equal(2, endpoint.Requests.Count);
    }

    [Fact]
    public async Task MsiEndpoint_FailuresPropagateWithoutInteractiveFallback()
    {
        await using var endpoint = new ManagedIdentityEndpoint { StatusCode = 400 };
        using var environment = new ManagedIdentityEnvironment(endpoint.Url);
        using var host = new CommandTestHost(headless: true);

        await Assert.ThrowsAnyAsync<MsalServiceException>(() =>
            CreateTokenService(host).AcquireAsync(Connection, Credential, default));

        Assert.Single(endpoint.Requests);
        Assert.Equal(0, host.Login.Calls);
    }

    [Fact]
    public async Task MsiEndpoint_InFlightRequestHonorsCancellation()
    {
        await using var endpoint = new ManagedIdentityEndpoint { WaitForCancellation = true };
        using var environment = new ManagedIdentityEnvironment(endpoint.Url);
        using var host = new CommandTestHost(headless: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var acquisition = CreateTokenService(host).AcquireAsync(Connection, Credential, cancellation.Token);

        await endpoint.RequestReceived.Task.WaitAsync(cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquisition);
        Assert.Equal(0, host.Login.Calls);
    }

    private static DataverseAccessTokenService CreateTokenService(CommandTestHost host)
        => new(new MsalClientFactory(),
            host.Provider.GetRequiredService<MsalTokenCacheBinder>(),
            host.Vault,
            host.Provider.GetRequiredService<IEnvironmentReader>(),
            host.Headless,
            host.BrowserProbe);

    private sealed class ManagedIdentityEnvironment : IDisposable
    {
        private readonly Dictionary<string, string?> _original = new();

        public ManagedIdentityEnvironment(string endpoint)
        {
            foreach (var name in new[]
            {
                "MSI_ENDPOINT", "MSI_SECRET", "IDENTITY_ENDPOINT", "IDENTITY_HEADER",
                "IDENTITY_SERVER_THUMBPRINT", "IMDS_ENDPOINT", "DEFAULT_IDENTITY_CLIENT_ID",
            })
            {
                _original[name] = System.Environment.GetEnvironmentVariable(name);
                System.Environment.SetEnvironmentVariable(name, name switch
                {
                    "MSI_ENDPOINT" => endpoint,
                    "DEFAULT_IDENTITY_CLIENT_ID" => "11111111-2222-3333-4444-555555555555",
                    _ => null,
                });
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in _original)
                System.Environment.SetEnvironmentVariable(name, value);
        }
    }

    private sealed record EndpointRequest(string Headers, string Body);

    // A real loopback endpoint exercises MSAL's MSI_ENDPOINT discovery and HTTP
    // protocol without adding a production abstraction just to mock MSAL.
    private sealed class ManagedIdentityEndpoint : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _server;
        public ConcurrentQueue<EndpointRequest> Requests { get; } = new();
        public TaskCompletionSource RequestReceived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StatusCode { get; init; } = 200;
        public TimeSpan Lifetime { get; init; } = TimeSpan.FromHours(1);
        public bool WaitForCancellation { get; init; }
        public string Url { get; }

        public ManagedIdentityEndpoint()
        {
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/token";
            _server = ServeAsync();
        }

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var headers = new StringBuilder();
                var contentLength = 0;
                while (await reader.ReadLineAsync(_stop.Token) is { Length: > 0 } line)
                {
                    headers.AppendLine(line);
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        contentLength = int.Parse(line["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                }

                var body = new char[contentLength];
                Assert.Equal(contentLength, await reader.ReadBlockAsync(body.AsMemory(), _stop.Token));
                Requests.Enqueue(new EndpointRequest(headers.ToString(), new string(body)));
                RequestReceived.TrySetResult();
                if (WaitForCancellation)
                    await Task.Delay(Timeout.Infinite, _stop.Token);

                var json = StatusCode == 200
                    ? JsonSerializer.Serialize(new
                    {
                        access_token = $"token-{Requests.Count}",
                        expires_on = DateTimeOffset.UtcNow.Add(Lifetime).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
                        token_type = "Bearer",
                        resource = Uri.UnescapeDataString(new string(body))["resource=".Length..],
                    })
                    : """{"error":"invalid_request","error_description":"Identity is not available"}""";
                var payload = Encoding.UTF8.GetBytes(json);
                var responseHeaders = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {StatusCode} {(StatusCode == 200 ? "OK" : "Bad Request")}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(responseHeaders, _stop.Token);
                await stream.WriteAsync(payload, _stop.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await _server;
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            finally
            {
                _listener.Stop();
                _stop.Dispose();
            }
        }
    }
}
