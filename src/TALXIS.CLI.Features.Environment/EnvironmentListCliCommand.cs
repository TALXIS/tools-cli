using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Logging;
using TALXIS.CLI.Platform.PowerPlatform.Control;

namespace TALXIS.CLI.Features.Environment;

[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List Dataverse environments you can access."
)]
public class EnvironmentListCliCommand : TxcLeafCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(EnvironmentListCliCommand));

    [CliOption(
        Name = "--credential",
        Aliases = ["-c"],
        Description = "Credential alias used to query environments. Defaults to the only stored credential; required when more than one exists.",
        Required = false
    )]
    public string? Credential { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var store = TxcServices.Get<ICredentialStore>();
        var credential = await ResolveCredentialAsync(store).ConfigureAwait(false);
        if (credential is null) return ExitValidationError;

        var connection = new Connection
        {
            Id = "(discovery)",
            Provider = ProviderKind.Dataverse,
            Cloud = credential.Cloud ?? CloudInstance.Public,
            TenantId = credential.TenantId,
        };

        var catalog = TxcServices.Get<IPowerPlatformEnvironmentCatalog>();
        var environments = await catalog.ListAsync(connection, credential, CancellationToken.None).ConfigureAwait(false);

        var rows = environments
            .OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(e => new EnvRow(
                e.DisplayName,
                e.EnvironmentUrl.ToString(),
                e.EnvironmentType?.ToString(),
                e.EnvironmentId,
                e.OrganizationId,
                credential.TenantId))
            .ToList();

        OutputFormatter.WriteList(rows, EnvironmentListPrinter.PrintTable);
        return ExitSuccess;
    }

    private async Task<Credential?> ResolveCredentialAsync(ICredentialStore store)
    {
        if (!string.IsNullOrWhiteSpace(Credential))
        {
            var alias = Credential.Trim();
            var match = await store.GetAsync(alias, CancellationToken.None).ConfigureAwait(false);
            if (match is null) Logger.LogError("Credential '{Alias}' was not found. Run 'txc config auth list' to see stored credentials.", alias);
            return match;
        }

        var all = await store.ListAsync(CancellationToken.None).ConfigureAwait(false);
        if (all.Count == 0)
        {
            Logger.LogError("No stored credentials. Sign in first with 'txc config auth login', then retry.");
            return null;
        }
        if (all.Count > 1)
        {
            var aliases = string.Join(", ", all.Select(c => c.Id).OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            Logger.LogError("Several credentials are stored ({Aliases}). Pick one with --credential <alias>.", aliases);
            return null;
        }
        return all[0];
    }
}

internal sealed record EnvRow(string Name, string Url, string? Type, Guid EnvironmentId, Guid? OrganizationId, string? TenantId);

internal static class EnvironmentListPrinter
{
    public static void PrintTable(IReadOnlyList<EnvRow> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No environments found.");
            return;
        }

        OutputWriter.WriteLine($"  {"NAME",-32} {"TYPE",-12} URL");
        foreach (var r in rows)
        {
            OutputWriter.WriteLine($"  {Trim(r.Name, 32),-32} {r.Type ?? "-",-12} {r.Url}");
        }
    }

    private static string Trim(string value, int max)
        => value.Length <= max ? value : value[..(max - 1)] + "...";
}
