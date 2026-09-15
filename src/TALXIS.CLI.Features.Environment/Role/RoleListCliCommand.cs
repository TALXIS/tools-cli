using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Role;

/// <summary>
/// Lists Dataverse security roles so you can find a role name or GUID to use
/// with the <c>--role</c> option on other <c>txc environment</c> commands.
/// Usage: <c>txc environment role list [--filter &lt;name&gt;]</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "list",
    Description = "List Dataverse security roles in the target environment."
)]
#pragma warning disable TXC003
public class RoleListCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(RoleListCliCommand));

    [CliOption(Name = "--filter", Description = "Optional role name contains-filter to help find a role for --role.", Required = false)]
    public string? Filter { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IDataverseRoleService>();
        var rows = await service.ListAsync(Profile, NormalizeFilter(Filter), CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteList(rows, PrintRolesTable);
        return ExitSuccess;
    }

    private static void PrintRolesTable(IReadOnlyList<DataverseRoleRecord> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No roles found.");
            return;
        }

        int nameWidth = Math.Clamp(rows.Max(r => r.Name.Length), 4, 50);
        int businessUnitWidth = Math.Clamp(rows.Max(r => (r.BusinessUnitName ?? "").Length), 13, 40);
        int idWidth = 36;

        string header =
            $"{"Name".PadRight(nameWidth)} | " +
            $"{"Business Unit".PadRight(businessUnitWidth)} | " +
            $"{"Id".PadRight(idWidth)}";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var row in rows)
        {
            OutputWriter.WriteLine(
                $"{Truncate(row.Name, nameWidth).PadRight(nameWidth)} | " +
                $"{Truncate(row.BusinessUnitName ?? "-", businessUnitWidth).PadRight(businessUnitWidth)} | " +
                $"{row.Id}");
        }
    }

    private static string? NormalizeFilter(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Truncate(string value, int maxWidth)
        => value.Length > maxWidth ? value[..(maxWidth - 1)] + "." : value;
}
#pragma warning restore TXC003
