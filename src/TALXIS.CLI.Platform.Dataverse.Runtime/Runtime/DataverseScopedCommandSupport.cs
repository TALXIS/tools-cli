using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;

namespace TALXIS.CLI.Platform.Dataverse.Runtime;

public static class DataverseScopedCommandSupport
{
    public static async Task<ResolvedProfileContext> ResolveContextAsync(
        string? profileName,
        Guid? environmentId,
        CancellationToken ct)
    {
        var resolver = TxcServices.Get<IConfigurationResolver>();
        var context = await resolver.ResolveAsync(profileName, ct).ConfigureAwait(false);
        if (!environmentId.HasValue)
            return context;

        var catalog = TxcServices.Get<IPowerPlatformEnvironmentCatalog>();
        var environment = (await catalog.ListAsync(context.Connection, context.Credential, ct).ConfigureAwait(false))
            .SingleOrDefault(candidate => candidate.EnvironmentId == environmentId.Value);

        if (environment is null)
        {
            throw new InvalidOperationException(
                $"Power Platform environment '{environmentId}' was not found or is not accessible with the resolved profile.");
        }

        return new ResolvedProfileContext(
            context.Profile,
            CloneConnection(context.Connection, environment),
            context.Credential,
            context.Source);
    }

    private static Connection CloneConnection(Connection source, PowerPlatformEnvironmentSummary environment)
        => new()
        {
            Id = source.Id,
            Provider = source.Provider,
            Description = source.Description,
            EnvironmentUrl = environment.EnvironmentUrl.AbsoluteUri,
            OrganizationId = environment.OrganizationId?.ToString(),
            EnvironmentId = environment.EnvironmentId,
            Cloud = source.Cloud,
            TenantId = source.TenantId,
            DisplayName = environment.DisplayName,
            EnvironmentType = environment.EnvironmentType,
            CreatedAt = source.CreatedAt,
            UpdatedAt = source.UpdatedAt,
            ExtraFields = source.ExtraFields is null
                ? null
                : new Dictionary<string, System.Text.Json.JsonElement>(source.ExtraFields, StringComparer.OrdinalIgnoreCase),
        };
}
