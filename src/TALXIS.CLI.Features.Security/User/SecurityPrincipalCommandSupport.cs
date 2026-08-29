using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;

namespace TALXIS.CLI.Features.Security;

internal static class SecurityPrincipalCommandSupport
{
    internal static Task<ResolvedProfileContext> ResolveContextAsync(string? profile, CancellationToken ct)
    {
        var configurationResolver = TxcServices.Get<IConfigurationResolver>();
        return configurationResolver.ResolveAsync(profile, ct);
    }

    internal static async Task<SecurityScopeContext> ResolveScopeAsync(
        string? profile,
        Guid? environmentId,
        CancellationToken ct)
    {
        var tenantContext = await ResolveContextAsync(profile, ct).ConfigureAwait(false);

        if (environmentId.HasValue)
        {
            var environment = await ResolveEnvironmentByIdAsync(tenantContext, environmentId.Value, ct).ConfigureAwait(false);
            return new SecurityScopeContext(
                tenantContext,
                CreateEnvironmentContext(tenantContext, environment),
                environment.EnvironmentId,
                environment.DisplayName,
                environment.EnvironmentUrl,
                true);
        }

        if (HasEnvironmentConnection(tenantContext.Connection))
        {
            var environment = await TryResolveEnvironmentAsync(tenantContext, ct).ConfigureAwait(false);
            return new SecurityScopeContext(
                tenantContext,
                tenantContext,
                environment?.EnvironmentId ?? tenantContext.Connection.EnvironmentId,
                environment?.DisplayName ?? tenantContext.Connection.DisplayName,
                environment?.EnvironmentUrl ?? ParseEnvironmentUrl(tenantContext.Connection.EnvironmentUrl),
                false);
        }

        return new SecurityScopeContext(tenantContext, null, null, null, null, false);
    }

    internal static async Task<SecurityScopeContext> ResolveRequiredEnvironmentScopeAsync(
        string? profile,
        Guid? environmentId,
        string commandPath,
        CancellationToken ct)
    {
        var scope = await ResolveScopeAsync(profile, environmentId, ct).ConfigureAwait(false);
        if (scope.EnvironmentContext is not null)
            return scope;

        throw new ConfigurationResolutionException(
            $"'{commandPath}' requires a Dataverse environment. Pass --environment <id> or use a profile connected to an environment.");
    }

    internal static bool TryHandleValidationException(ILogger logger, Exception ex, out int exitCode)
    {
        if (ex is TenantPrincipalAmbiguousException ambiguousPrincipal)
        {
            logger.LogError("{Error}", ambiguousPrincipal.Message);
            foreach (var candidate in ambiguousPrincipal.Candidates)
                logger.LogError("Candidate: {Candidate}", candidate);

            exitCode = 2;
            return true;
        }

        if (ex is TenantRoleAmbiguousException ambiguousRole)
        {
            logger.LogError("{Error}", ambiguousRole.Message);
            foreach (var candidate in ambiguousRole.CandidateNames)
                logger.LogError("Candidate: {Candidate}", candidate);

            exitCode = 2;
            return true;
        }

        if (ex is DataverseAmbiguousMatchException ambiguousDataverse)
        {
            logger.LogError("Multiple {EntityDisplayName} records matched '{Identifier}'.", ambiguousDataverse.EntityDisplayName, ambiguousDataverse.Identifier);
            foreach (var candidate in ambiguousDataverse.Candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate.Description))
                {
                    logger.LogError("  - {Name} ({Id})", candidate.Name, candidate.Id);
                }
                else
                {
                    logger.LogError("  - {Name} [{Description}] ({Id})", candidate.Name, candidate.Description, candidate.Id);
                }
            }

            exitCode = 2;
            return true;
        }

        if (ex is ArgumentException or InvalidOperationException)
        {
            logger.LogError("{Error}", ex.Message);
            exitCode = 2;
            return true;
        }

        exitCode = 0;
        return false;
    }

    internal static void WriteRoleTable(IReadOnlyList<PowerPlatformTenantRoleAssignment> assignments)
    {
#pragma warning disable TXC003
        if (assignments.Count == 0)
        {
            OutputWriter.WriteLine("No tenant roles assigned.");
            return;
        }

        int roleNameWidth = Math.Clamp(assignments.Max(a => a.RoleName.Length), 9, 36);
        int roleIdWidth = Math.Clamp(assignments.Max(a => a.RoleIdentifier.Length), 7, 36);
        int scopeWidth = Math.Clamp(assignments.Max(a => a.Scope.Length), 5, 48);

        string header =
            $"{"Role Name".PadRight(roleNameWidth)} | " +
            $"{"Role ID".PadRight(roleIdWidth)} | " +
            "Scope";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var assignment in assignments)
        {
            OutputWriter.WriteLine(
                $"{Truncate(assignment.RoleName, roleNameWidth).PadRight(roleNameWidth)} | " +
                $"{Truncate(assignment.RoleIdentifier, roleIdWidth).PadRight(roleIdWidth)} | " +
                $"{Truncate(assignment.Scope, scopeWidth)}");
        }
#pragma warning restore TXC003
    }

    internal static void WriteCombinedRoleSections(
        IReadOnlyList<PowerPlatformTenantRoleAssignment> tenantAssignments,
        IReadOnlyList<DataverseRoleRecord> environmentAssignments,
        SecurityScopeContext scope,
        Action<IReadOnlyList<DataverseRoleRecord>> environmentRenderer)
    {
#pragma warning disable TXC003
        OutputWriter.WriteLine("Tenant admin roles");
        OutputWriter.WriteLine("------------------");
        WriteRoleTable(tenantAssignments);
        OutputWriter.WriteLine();
        OutputWriter.WriteLine($"Environment security roles ({FormatEnvironmentLabel(scope)})");
        OutputWriter.WriteLine(new string('-', $"Environment security roles ({FormatEnvironmentLabel(scope)})".Length));
        environmentRenderer(environmentAssignments);
#pragma warning restore TXC003
    }

    internal static void WriteMutationResult<T>(T payload, Action textRenderer)
        => OutputFormatter.WriteData(payload, _ => textRenderer());

    internal static bool TryResolveStateFilter(
        bool enabled,
        bool disabled,
        bool all,
        ILogger logger,
        out DataverseSecurityPrincipalStateFilter filter)
    {
        var selected = (enabled ? 1 : 0) + (disabled ? 1 : 0) + (all ? 1 : 0);
        if (selected > 1)
        {
            logger.LogError("Specify at most one of --enabled, --disabled, or --all.");
            filter = default;
            return false;
        }

        filter = disabled
            ? DataverseSecurityPrincipalStateFilter.Disabled
            : all
                ? DataverseSecurityPrincipalStateFilter.All
                : DataverseSecurityPrincipalStateFilter.Enabled;
        return true;
    }

    internal static bool TryParseRoleIdentifiers(
        string? csv,
        ILogger logger,
        out IReadOnlyList<string> roles)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            roles = Array.Empty<string>();
            return true;
        }

        var parsed = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (parsed.Length == 0)
        {
            logger.LogError("--role must contain at least one role name or GUID when specified.");
            roles = Array.Empty<string>();
            return false;
        }

        roles = parsed;
        return true;
    }

    internal static bool IsRoleMatch(DataverseRoleRecord role, string roleNameOrGuid)
        => string.Equals(role.Id.ToString(), roleNameOrGuid, StringComparison.OrdinalIgnoreCase)
            || string.Equals(role.Name, roleNameOrGuid, StringComparison.OrdinalIgnoreCase);

    internal static string Truncate(string value, int maxWidth)
        => value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;

    internal static string FormatEnvironmentLabel(SecurityScopeContext scope)
        => scope.EnvironmentId?.ToString()
            ?? scope.EnvironmentUrl?.AbsoluteUri
            ?? scope.EnvironmentDisplayName
            ?? "active environment";

    private static bool HasEnvironmentConnection(Connection connection)
        => connection.EnvironmentId.HasValue || !string.IsNullOrWhiteSpace(connection.EnvironmentUrl);

    private static async Task<PowerPlatformEnvironmentSummary?> TryResolveEnvironmentAsync(
        ResolvedProfileContext context,
        CancellationToken ct)
    {
        var catalog = TxcServices.Get<IPowerPlatformEnvironmentCatalog>();
        var environments = await catalog.ListAsync(context.Connection, context.Credential, ct).ConfigureAwait(false);

        if (context.Connection.EnvironmentId.HasValue)
            return environments.SingleOrDefault(candidate => candidate.EnvironmentId == context.Connection.EnvironmentId.Value);

        var environmentUrl = ParseEnvironmentUrl(context.Connection.EnvironmentUrl);
        return environmentUrl is null
            ? null
            : environments.SingleOrDefault(candidate => UrlEquals(candidate.EnvironmentUrl, environmentUrl));
    }

    private static async Task<PowerPlatformEnvironmentSummary> ResolveEnvironmentByIdAsync(
        ResolvedProfileContext context,
        Guid environmentId,
        CancellationToken ct)
    {
        var catalog = TxcServices.Get<IPowerPlatformEnvironmentCatalog>();
        return (await catalog.ListAsync(context.Connection, context.Credential, ct).ConfigureAwait(false))
            .SingleOrDefault(candidate => candidate.EnvironmentId == environmentId)
            ?? throw new InvalidOperationException(
                $"Power Platform environment '{environmentId}' was not found or is not accessible with the resolved profile.");
    }

    private static ResolvedProfileContext CreateEnvironmentContext(
        ResolvedProfileContext source,
        PowerPlatformEnvironmentSummary environment)
        => new(
            source.Profile,
            new Connection
            {
                Id = source.Connection.Id,
                Provider = source.Connection.Provider,
                Description = source.Connection.Description,
                EnvironmentUrl = environment.EnvironmentUrl.AbsoluteUri,
                OrganizationId = environment.OrganizationId?.ToString(),
                EnvironmentId = environment.EnvironmentId,
                Cloud = source.Connection.Cloud,
                TenantId = source.Connection.TenantId,
                DisplayName = environment.DisplayName,
                EnvironmentType = environment.EnvironmentType,
                CreatedAt = source.Connection.CreatedAt,
                UpdatedAt = source.Connection.UpdatedAt,
                ExtraFields = source.Connection.ExtraFields,
            },
            source.Credential,
            source.Source);

    private static Uri? ParseEnvironmentUrl(string? environmentUrl)
        => Uri.TryCreate(environmentUrl, UriKind.Absolute, out var uri)
            ? NormalizeEnvironmentUrl(uri)
            : null;

    private static bool UrlEquals(Uri left, Uri right)
        => NormalizeEnvironmentUrl(left).AbsoluteUri.Equals(
            NormalizeEnvironmentUrl(right).AbsoluteUri,
            StringComparison.OrdinalIgnoreCase);

    private static Uri NormalizeEnvironmentUrl(Uri uri)
        => new(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
}

internal sealed record SecurityScopeContext(
    ResolvedProfileContext TenantContext,
    ResolvedProfileContext? EnvironmentContext,
    Guid? EnvironmentId,
    string? EnvironmentDisplayName,
    Uri? EnvironmentUrl,
    bool ExplicitEnvironment)
{
    public bool HasEnvironment => EnvironmentContext is not null;
}
