using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;

namespace TALXIS.CLI.Features.Environment.Team;

internal static class TeamCommandSupport
{
    private static readonly Dictionary<string, DataverseTeamType> TeamTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["owner"] = DataverseTeamType.Owner,
        ["access"] = DataverseTeamType.Access,
        ["aad-security-group"] = DataverseTeamType.AadSecurityGroup,
        ["aad-office-group"] = DataverseTeamType.AadOfficeGroup,
    };

    private static readonly Dictionary<string, DataverseTeamMembershipType> MembershipTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["members-and-guests"] = DataverseTeamMembershipType.MembersAndGuests,
        ["members"] = DataverseTeamMembershipType.Members,
        ["owners"] = DataverseTeamMembershipType.Owners,
        ["guests"] = DataverseTeamMembershipType.Guests,
    };

    public static bool TryParseTeamType(string value, ILogger logger, out DataverseTeamType teamType)
    {
        if (TeamTypes.TryGetValue(value.Trim(), out teamType))
            return true;

        logger.LogError(
            "Invalid --type value '{Value}'. Valid values: owner, access, aad-security-group, aad-office-group.",
            value);
        return false;
    }

    public static bool TryParseMembershipType(string value, ILogger logger, out DataverseTeamMembershipType membershipType)
    {
        if (MembershipTypes.TryGetValue(value.Trim(), out membershipType))
            return true;

        logger.LogError(
            "Invalid --membership-type value '{Value}'. Valid values: members-and-guests, members, owners, guests.",
            value);
        return false;
    }

    public static bool TryParseGuidOption(string value, string optionName, ILogger logger, out Guid guid)
    {
        if (Guid.TryParse(value, out guid))
            return true;

        logger.LogError("{OptionName} must be a valid GUID. Got '{Value}'.", optionName, value);
        return false;
    }

    public static bool IsAadManaged(DataverseTeamType teamType)
        => teamType is DataverseTeamType.AadSecurityGroup or DataverseTeamType.AadOfficeGroup;

    public static string ToCliValue(DataverseTeamType teamType) => teamType switch
    {
        DataverseTeamType.Owner => "owner",
        DataverseTeamType.Access => "access",
        DataverseTeamType.AadSecurityGroup => "aad-security-group",
        DataverseTeamType.AadOfficeGroup => "aad-office-group",
        _ => teamType.ToString(),
    };

    public static string? ToCliValue(DataverseTeamMembershipType? membershipType) => membershipType switch
    {
        DataverseTeamMembershipType.MembersAndGuests => "members-and-guests",
        DataverseTeamMembershipType.Members => "members",
        DataverseTeamMembershipType.Owners => "owners",
        DataverseTeamMembershipType.Guests => "guests",
        null => null,
        _ => membershipType.ToString(),
    };

    public static int HandleDataverseValidationException(ILogger logger, Exception ex, int exitValidationError)
    {
        logger.LogError(ex, "{Error}", ex.Message);
        return exitValidationError;
    }

    public static void WriteTeamDetail(DataverseTeamRecord team)
    {
#pragma warning disable TXC003
        OutputWriter.WriteLine($"Name:              {team.Name}");
        OutputWriter.WriteLine($"Id:                {team.Id}");
        OutputWriter.WriteLine($"Type:              {ToCliValue(team.TeamType)}");
        OutputWriter.WriteLine($"AAD Object Id:     {team.AzureActiveDirectoryObjectId?.ToString() ?? "-"}");
        OutputWriter.WriteLine($"Membership Type:   {ToCliValue(team.MembershipType) ?? "-"}");
        OutputWriter.WriteLine($"Business Unit:     {team.BusinessUnitName ?? "-"}");
        OutputWriter.WriteLine($"Business Unit Id:  {team.BusinessUnitId?.ToString() ?? "-"}");
        OutputWriter.WriteLine($"Default Team:      {(team.IsDefault ? "true" : "false")}");
        OutputWriter.WriteLine($"System Managed:    {(team.IsSystemManaged ? "true" : "false")}");
#pragma warning restore TXC003
    }

    public static void WriteTeamList(IReadOnlyList<DataverseTeamRecord> rows)
    {
        if (rows.Count == 0)
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine("No teams found.");
#pragma warning restore TXC003
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => r.Name.Length), 4, 40);
        int typeWidth = Math.Clamp(rows.Max(r => ToCliValue(r.TeamType).Length), 4, 18);
        int membershipWidth = Math.Clamp(rows.Max(r => (ToCliValue(r.MembershipType) ?? "-").Length), 10, 20);
        int businessUnitWidth = Math.Clamp(rows.Max(r => (r.BusinessUnitName ?? "-").Length), 13, 28);
        const int defaultWidth = 7;
        const int managedWidth = 14;

        string header =
            $"{"Name".PadRight(nameWidth)} | " +
            $"{"Type".PadRight(typeWidth)} | " +
            $"{"Membership".PadRight(membershipWidth)} | " +
            $"{"Business Unit".PadRight(businessUnitWidth)} | " +
            $"{"Default".PadRight(defaultWidth)} | " +
            $"{"System Managed".PadRight(managedWidth)}";
#pragma warning disable TXC003
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var row in rows)
        {
            OutputWriter.WriteLine(
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.Name, nameWidth).PadRight(nameWidth)} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(ToCliValue(row.TeamType), typeWidth).PadRight(typeWidth)} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(ToCliValue(row.MembershipType) ?? "-", membershipWidth).PadRight(membershipWidth)} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.BusinessUnitName ?? "-", businessUnitWidth).PadRight(businessUnitWidth)} | " +
                $"{(row.IsDefault ? "true" : "false").PadRight(defaultWidth)} | " +
                $"{(row.IsSystemManaged ? "true" : "false").PadRight(managedWidth)}");
        }
#pragma warning restore TXC003
    }

    public static void WriteMemberList(IReadOnlyList<DataverseUserRecord> rows)
    {
        if (rows.Count == 0)
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine("No team members found.");
#pragma warning restore TXC003
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => (r.FullName ?? "-").Length), 4, 28);
        int upnWidth = Math.Clamp(rows.Max(r => (r.UserPrincipalName ?? "-").Length), 3, 36);
        int emailWidth = Math.Clamp(rows.Max(r => (r.PrimaryEmailAddress ?? "-").Length), 5, 36);
        int businessUnitWidth = Math.Clamp(rows.Max(r => (r.BusinessUnitName ?? "-").Length), 13, 24);
        const int disabledWidth = 8;

        string header =
            $"{"Name".PadRight(nameWidth)} | " +
            $"{"UPN".PadRight(upnWidth)} | " +
            $"{"Email".PadRight(emailWidth)} | " +
            $"{"Business Unit".PadRight(businessUnitWidth)} | " +
            $"{"Disabled".PadRight(disabledWidth)}";
#pragma warning disable TXC003
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var row in rows)
        {
            OutputWriter.WriteLine(
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.FullName ?? "-", nameWidth).PadRight(nameWidth)} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.UserPrincipalName ?? "-", upnWidth).PadRight(upnWidth)} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.PrimaryEmailAddress ?? "-", emailWidth).PadRight(emailWidth)} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.BusinessUnitName ?? "-", businessUnitWidth).PadRight(businessUnitWidth)} | " +
                $"{(row.IsDisabled ? "true" : "false").PadRight(disabledWidth)}");
        }
#pragma warning restore TXC003
    }

    public static void WriteRoleList(IReadOnlyList<DataverseRoleRecord> rows)
    {
        if (rows.Count == 0)
        {
#pragma warning disable TXC003
            OutputWriter.WriteLine("No team roles found.");
#pragma warning restore TXC003
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => r.Name.Length), 4, 40);
        int businessUnitWidth = Math.Clamp(rows.Max(r => (r.BusinessUnitName ?? "-").Length), 13, 28);

        string header =
            $"{"Name".PadRight(nameWidth)} | " +
            $"{"Business Unit".PadRight(businessUnitWidth)} | Id";
#pragma warning disable TXC003
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var row in rows)
        {
            OutputWriter.WriteLine(
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.Name, nameWidth).PadRight(nameWidth)} | " +
                $"{EnvironmentPrincipalCommandSupport.Truncate(row.BusinessUnitName ?? "-", businessUnitWidth).PadRight(businessUnitWidth)} | " +
                row.Id);
        }
#pragma warning restore TXC003
    }
}
