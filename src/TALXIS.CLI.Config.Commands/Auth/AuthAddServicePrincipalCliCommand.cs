using System.Text;
using System.Text.Json;
using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Headless;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Shared;

namespace TALXIS.CLI.Config.Commands.Auth;

/// <summary>
/// <c>txc config auth add-service-principal</c> — register a client-secret
/// Entra app registration as a reusable <see cref="Credential"/>. The
/// secret value is read via one of (in order):
/// <c>--secret-from-env</c> → piped stdin → masked TTY prompt.
/// It is then written to the OS credential vault; only a
/// <see cref="SecretRef"/> handle is persisted in the credential store.
/// </summary>
[CliCommand(
    Name = "add-service-principal",
    Aliases = new[] { "add-sp" },
    Description = "Register a client-secret service principal credential."
)]
public class AuthAddServicePrincipalCliCommand
{
    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(AuthAddServicePrincipalCliCommand));

    [CliOption(Name = "--alias", Description = "Credential alias used to reference this service principal.", Required = true)]
    public string Alias { get; set; } = string.Empty;

    [CliOption(Name = "--tenant", Description = "Entra tenant id or domain.", Required = true)]
    public string Tenant { get; set; } = string.Empty;

    [CliOption(Name = "--application-id", Aliases = new[] { "--app-id", "--client-id" }, Description = "Entra application (client) id.", Required = true)]
    public string ApplicationId { get; set; } = string.Empty;

    [CliOption(Name = "--cloud", Description = "Sovereign cloud. Default: public.", Required = false)]
    public CloudInstance? Cloud { get; set; }

    [CliOption(Name = "--description", Description = "Free-form label shown in 'config auth list'.", Required = false)]
    public string? Description { get; set; }

    [CliOption(Name = "--secret-from-env", Description = "Name of an environment variable holding the client secret.", Required = false)]
    public string? SecretFromEnv { get; set; }

    public async Task<int> RunAsync()
    {
        try
        {
            var headless = TxcServices.Get<IHeadlessDetector>();
            headless.EnsureKindAllowed(CredentialKind.ClientSecret);

            var alias = Alias.Trim();
            if (string.IsNullOrEmpty(alias))
            {
                _logger.LogError("--alias must not be empty.");
                return 1;
            }

            var secret = ReadSecret(SecretFromEnv, _logger);
            if (secret is null) return 1;

            var store = TxcServices.Get<ICredentialStore>();
            var vault = TxcServices.Get<ICredentialVault>();

            var secretRef = SecretRef.Create(alias, "client-secret");
            await vault.SetSecretAsync(secretRef, secret, CancellationToken.None).ConfigureAwait(false);

            var credential = new Credential
            {
                Id = alias,
                Kind = CredentialKind.ClientSecret,
                TenantId = Tenant.Trim(),
                ApplicationId = ApplicationId.Trim(),
                Cloud = Cloud ?? CloudInstance.Public,
                Description = Description,
                SecretRef = secretRef,
            };
            await store.UpsertAsync(credential, CancellationToken.None).ConfigureAwait(false);

            _logger.LogInformation("Saved service-principal credential '{Alias}' (app {AppId}, tenant {Tenant}).",
                alias, credential.ApplicationId, credential.TenantId);

            OutputWriter.WriteLine(JsonSerializer.Serialize(
                new
                {
                    id = credential.Id,
                    kind = credential.Kind.ToString(),
                    tenantId = credential.TenantId,
                    applicationId = credential.ApplicationId,
                    cloud = credential.Cloud,
                    description = credential.Description,
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                }));
            return 0;
        }
        catch (HeadlessAuthRequiredException ex)
        {
            _logger.LogError("{Message}", ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register service-principal credential.");
            return 1;
        }
    }

    /// <summary>
    /// Test seam: when non-null, <see cref="ReadSecret"/> treats this as
    /// a redirected stdin and reads its first line instead of consulting
    /// <see cref="Console.IsInputRedirected"/> / <see cref="Console.In"/>.
    /// Production callers leave this null.
    /// </summary>
    internal static TextReader? StdinOverride { get; set; }

    /// <summary>
    /// Resolves the client secret from (in order): the named env var,
    /// redirected stdin, or an interactive masked TTY prompt. Returns
    /// null and logs an error when no source is available. Internal
    /// for testability.
    /// </summary>
    internal static string? ReadSecret(string? secretFromEnv, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(secretFromEnv))
        {
            var value = System.Environment.GetEnvironmentVariable(secretFromEnv);
            if (string.IsNullOrEmpty(value))
            {
                logger.LogError("Environment variable '{Var}' is not set or empty.", secretFromEnv);
                return null;
            }
            return value;
        }

        var stdin = StdinOverride;
        if (stdin is not null || Console.IsInputRedirected)
        {
            var reader = stdin ?? Console.In;
            var piped = reader.ReadLine();
            if (string.IsNullOrEmpty(piped))
            {
                logger.LogError("Stdin was redirected but no secret was read. Pipe the secret value or pass --secret-from-env.");
                return null;
            }
            return piped;
        }

        if (!Console.IsOutputRedirected)
        {
            return PromptMaskedSecret();
        }

        logger.LogError(
            "No client secret provided. Use --secret-from-env <VARNAME>, pipe the secret via stdin, or run in an interactive terminal.");
        return null;
    }

    private static string PromptMaskedSecret()
    {
        Console.Write("Client secret: ");
        var buffer = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) buffer.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar))
            {
                buffer.Append(key.KeyChar);
            }
        }
        return buffer.ToString();
    }
}
