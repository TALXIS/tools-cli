using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;

namespace TALXIS.CLI.Features.Security.Role;

internal static class RoleOutput
{
#pragma warning disable TXC003
    public static void PrintDetailList(IReadOnlyList<PowerPlatformRoleDefinition> roles)
    {
        if (roles.Count == 0)
        {
            OutputWriter.WriteLine("No tenant roles found.");
            return;
        }

        int nameWidth = Math.Clamp(roles.Max(r => r.RoleDefinitionName.Length), 9, 36);
        int idWidth = 36;
        int descriptionWidth = Math.Clamp(roles.Max(r => (r.Description ?? string.Empty).Length), 11, 80);

        string header =
            $"{"Role Name".PadRight(nameWidth)} | " +
            $"{"Role ID".PadRight(idWidth)} | " +
            "Description";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var role in roles)
        {
            OutputWriter.WriteLine(
                $"{Truncate(role.RoleDefinitionName, nameWidth).PadRight(nameWidth)} | " +
                $"{role.RoleDefinitionId} | " +
                $"{Truncate(role.Description ?? string.Empty, descriptionWidth)}");
        }
    }

    public static void PrintDetail(PowerPlatformRoleDefinition role)
    {
        OutputWriter.WriteLine($"Role Name:    {role.RoleDefinitionName}");
        OutputWriter.WriteLine($"Role ID:      {role.RoleDefinitionId}");
        OutputWriter.WriteLine($"Description:  {role.Description ?? "-"}");
        OutputWriter.WriteLine($"Scopes:       {(role.AssignableScopes.Count == 0 ? "-" : string.Join(", ", role.AssignableScopes))}");
    }

    private static string Truncate(string value, int maxWidth)
        => value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;
#pragma warning restore TXC003
}
