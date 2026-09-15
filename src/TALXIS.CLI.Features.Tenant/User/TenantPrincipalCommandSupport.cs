using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;

namespace TALXIS.CLI.Features.Tenant;

internal static class TenantPrincipalCommandSupport
{
    internal static Task<ResolvedProfileContext> ResolveContextAsync(string? profile, CancellationToken ct)
    {
        var configurationResolver = TxcServices.Get<IConfigurationResolver>();
        return configurationResolver.ResolveAsync(profile, ct);
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

    internal static void WriteMutationResult<T>(T payload, Action textRenderer)
        => OutputFormatter.WriteData(payload, _ => textRenderer());

    internal static string Truncate(string value, int maxWidth)
        => value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;
}
