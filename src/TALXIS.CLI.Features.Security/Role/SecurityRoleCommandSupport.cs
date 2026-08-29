using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Platform.PowerPlatform.Control;

namespace TALXIS.CLI.Features.Security.Role;

internal static class SecurityRoleCommandSupport
{
    public static async Task<IReadOnlyList<PowerPlatformRoleDefinition>> ListTenantRolesAsync(
        string? profile,
        string? filter,
        CancellationToken ct)
    {
        var context = await SecurityPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<SecurityRoleResolver>();
        return await resolver.ListTenantRolesAsync(context.Connection, context.Credential, filter, ct).ConfigureAwait(false);
    }

    public static async Task<PowerPlatformRoleDefinition> GetTenantRoleAsync(
        string? profile,
        string role,
        CancellationToken ct)
    {
        var context = await SecurityPrincipalCommandSupport.ResolveContextAsync(profile, ct).ConfigureAwait(false);
        var resolver = TxcServices.Get<SecurityRoleResolver>();
        return await resolver.GetTenantRoleAsync(context.Connection, context.Credential, role, ct).ConfigureAwait(false);
    }

    public static void PrintEnvironmentRoleList(IReadOnlyList<DataverseRoleRecord> rows)
    {
#pragma warning disable TXC003
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No roles found.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => r.Name.Length), 4, 50);
        int businessUnitWidth = Math.Clamp(rows.Max(r => (r.BusinessUnitName ?? string.Empty).Length), 13, 40);
        const int idWidth = 36;

        string header =
            $"{"Name".PadRight(nameWidth)} | " +
            $"{"Business Unit".PadRight(businessUnitWidth)} | " +
            $"{"Id".PadRight(idWidth)}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var row in rows)
        {
            OutputWriter.WriteLine(
                $"{SecurityPrincipalCommandSupport.Truncate(row.Name, nameWidth).PadRight(nameWidth)} | " +
                $"{SecurityPrincipalCommandSupport.Truncate(row.BusinessUnitName ?? "-", businessUnitWidth).PadRight(businessUnitWidth)} | " +
                $"{row.Id}");
        }
#pragma warning restore TXC003
    }

    public static void PrintEnvironmentRoleDetail(DataverseRoleRecord role)
    {
#pragma warning disable TXC003
        OutputWriter.WriteLine($"Name:          {role.Name}");
        OutputWriter.WriteLine($"Business Unit: {role.BusinessUnitName ?? "-"}");
        OutputWriter.WriteLine($"Id:            {role.Id}");
#pragma warning restore TXC003
    }
}
