using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Features.Environment.ServicePrincipal;

internal static class ServicePrincipalCommandSupport
{
    internal static bool TryResolveStateFilter(
        bool enabled,
        bool disabled,
        bool all,
        ILogger logger,
        out DataverseSecurityPrincipalStateFilter filter)
        => EnvironmentPrincipalCommandSupport.TryResolveStateFilter(enabled, disabled, all, logger, out filter);

    internal static bool TryResolveEnabledState(
        bool enable,
        bool disable,
        ILogger logger,
        out bool enabled)
    {
        if (enable == disable)
        {
            logger.LogError("Specify exactly one of --enable or --disable.");
            enabled = false;
            return false;
        }

        enabled = enable;
        return true;
    }

    internal static bool TryParseRoleIdentifiers(
        string? csv,
        ILogger logger,
        out IReadOnlyList<string> roles)
        => EnvironmentPrincipalCommandSupport.TryParseRoleIdentifiers(csv, logger, out roles);

    internal static bool TryHandleValidationException(ILogger logger, Exception ex, out int exitCode)
        => EnvironmentPrincipalCommandSupport.TryHandleValidationException(logger, ex, LogAmbiguousMatch, out exitCode);

    private static void LogAmbiguousMatch(ILogger logger, DataverseAmbiguousMatchException ambiguous)
    {
        logger.LogError("{Error}", ambiguous.Message);
        foreach (var candidate in ambiguous.Candidates)
        {
            logger.LogError(
                "Candidate: {Name} ({Id}){Description}",
                candidate.Name,
                candidate.Id,
                string.IsNullOrWhiteSpace(candidate.Description)
                    ? string.Empty
                    : $" — {candidate.Description}");
        }
    }

    internal static void WriteServicePrincipalDetails(DataverseServicePrincipalRecord app)
    {
#pragma warning disable TXC003
        OutputWriter.WriteLine($"System User ID: {app.Id}");
        OutputWriter.WriteLine($"Application ID: {app.ApplicationId}");
        OutputWriter.WriteLine($"Name:           {app.FullName ?? "-"}");
        OutputWriter.WriteLine($"State:          {(app.IsDisabled ? "Disabled" : "Enabled")}");
        OutputWriter.WriteLine($"Business Unit:  {app.BusinessUnitName ?? "-"}");
        OutputWriter.WriteLine($"Business Unit ID: {(app.BusinessUnitId?.ToString() ?? "-")}");
        OutputWriter.WriteLine($"Entra Object ID: {(app.AzureActiveDirectoryObjectId?.ToString() ?? "-")}");
#pragma warning restore TXC003
    }

    internal static void WriteAppTable(IReadOnlyList<DataverseServicePrincipalRecord> rows)
    {
#pragma warning disable TXC003
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No service principals found.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(static r => (r.FullName ?? string.Empty).Length), 4, 36);
        int stateWidth = 8;
        int businessUnitWidth = Math.Clamp(rows.Max(static r => (r.BusinessUnitName ?? string.Empty).Length), 13, 36);

        string header =
            $"{"System User ID".PadRight(36)} | " +
            $"{"Application ID".PadRight(36)} | " +
            $"{"Name".PadRight(nameWidth)} | " +
            $"{"State".PadRight(stateWidth)} | " +
            $"{"Business Unit".PadRight(businessUnitWidth)}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var row in rows)
        {
            OutputWriter.WriteLine(
                $"{row.Id} | " +
                $"{row.ApplicationId} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.FullName ?? string.Empty, nameWidth).PadRight(nameWidth)} | " +
                $"{(row.IsDisabled ? "Disabled" : "Enabled").PadRight(stateWidth)} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.BusinessUnitName ?? string.Empty, businessUnitWidth).PadRight(businessUnitWidth)}");
        }
#pragma warning restore TXC003
    }

    internal static void WriteRoleTable(IReadOnlyList<DataverseRoleRecord> rows)
    {
#pragma warning disable TXC003
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No security roles assigned.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(static r => r.Name.Length), 4, 48);
        int businessUnitWidth = Math.Clamp(rows.Max(static r => (r.BusinessUnitName ?? string.Empty).Length), 13, 36);

        string header =
            $"{"Role ID".PadRight(36)} | " +
            $"{"Name".PadRight(nameWidth)} | " +
            $"{"Business Unit".PadRight(businessUnitWidth)}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var row in rows)
        {
            OutputWriter.WriteLine(
                $"{row.Id} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.Name, nameWidth).PadRight(nameWidth)} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.BusinessUnitName ?? string.Empty, businessUnitWidth).PadRight(businessUnitWidth)}");
        }
#pragma warning restore TXC003
    }

    internal static void WriteCreateResult(
        DataverseServicePrincipalRecord app,
        IReadOnlyList<string> assignedRoles,
        IReadOnlyList<ServicePrincipalRoleAssignmentFailure> failures)
    {
        var payload = new
        {
            status = failures.Count == 0 ? "created" : "partial",
            servicePrincipal = app,
            assignedRoles,
            failedRoles = failures.Select(static failure => new
            {
                role = failure.Role,
                error = failure.Message,
            }).ToArray(),
        };

        OutputFormatter.WriteData(payload, _ =>
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine(failures.Count == 0
                ? "Service principal created."
                : "Service principal created, but one or more role assignments failed.");
            WriteServicePrincipalDetails(app);

            if (assignedRoles.Count > 0)
            {
                OutputWriter.WriteLine();
                OutputWriter.WriteLine($"Assigned roles ({assignedRoles.Count}):");
                foreach (var role in assignedRoles)
                    OutputWriter.WriteLine($"  - {role}");
            }

            if (failures.Count > 0)
            {
                OutputWriter.WriteLine();
                OutputWriter.WriteLine($"Role assignment failures ({failures.Count}):");
                foreach (var failure in failures)
                    OutputWriter.WriteLine($"  - {failure.Role}: {failure.Message}");
            }
#pragma warning restore TXC003
        });
    }

    internal static void WriteMutationResult<T>(T payload, Action textRenderer)
    {
        OutputFormatter.WriteData(payload, _ => textRenderer());
    }
}

internal sealed record ServicePrincipalRoleAssignmentFailure(string Role, string Message, bool IsValidationError);
