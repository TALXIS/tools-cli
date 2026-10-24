using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control;

namespace TALXIS.CLI.Features.Governance.EnvironmentGroup;

internal static class EnvironmentGroupRoleOutput
{
#pragma warning disable TXC003
    public static void PrintList(IReadOnlyList<PowerPlatformEnvironmentGroupRoleAssignment> assignments)
    {
        if (assignments.Count == 0)
        {
            OutputWriter.WriteLine("No role assignments found on this environment group.");
            return;
        }

        string header = $"{"Principal Type".PadRight(14)} | {"Principal Object ID".PadRight(36)} | {"Role Definition ID".PadRight(36)} | Assignment ID";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var assignment in assignments)
        {
            OutputWriter.WriteLine(
                $"{assignment.PrincipalType.ToString().PadRight(14)} | " +
                $"{assignment.PrincipalObjectId.ToString().PadRight(36)} | " +
                $"{assignment.RoleDefinitionId.ToString().PadRight(36)} | " +
                assignment.RoleAssignmentId);
        }
    }
#pragma warning restore TXC003

    public static void WriteMutationResult<T>(T payload, Action textRenderer)
        => OutputFormatter.WriteData(payload, _ => textRenderer());

    public static bool TryHandleValidationException(ILogger logger, Exception ex, out int exitCode)
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
}
