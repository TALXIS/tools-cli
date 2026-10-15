using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Connection.Permission;

/// <summary>
/// Lists the owner and everyone the connection is shared with.
/// Usage: <c>txc environment connection permission list &lt;connection-id&gt; [--connector &lt;name&gt;]</c>
/// </summary>
[CliReadOnly]
[CliWorkflow("environment-inspection")]
[CliCommand(
    Name = "list",
    Description = "Lists who can use a connection in the LIVE connected environment. Requires an active profile."
)]
public class ConnectionPermissionListCliCommand : ConnectionCommandBase
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ConnectionPermissionListCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var service = TxcServices.Get<IConnectionPermissionService>();
        var permissions = await service.ListPermissionsAsync(Profile, Connector, ConnectionId, CancellationToken.None).ConfigureAwait(false);

        var rows = permissions.Select(ToRow).ToList();
        OutputFormatter.WriteList(rows, PrintTable);
        return ExitSuccess;
    }

    internal static PermissionRow ToRow(ConnectionPermission p)
        => new(p.PermissionId, p.PrincipalId, p.PrincipalType, p.DisplayName, p.Email, ConnectionRoleNames.ToCli(p.Role));

    /// <summary>Row shape of the permission list: the role in the CLI's vocabulary.</summary>
    internal sealed record PermissionRow(
        string PermissionId, string? PrincipalId, string? PrincipalType, string? DisplayName, string? Email, string Role);

    // Text-renderer callback invoked by OutputFormatter.WriteList — OutputWriter usage is intentional.
#pragma warning disable TXC003
    private static void PrintTable(IReadOnlyList<PermissionRow> rows)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No permissions found.");
            return;
        }

        var header = $"{"Principal".PadRight(32)} | {"Type".PadRight(16)} | {"Role".PadRight(20)} | Principal id";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var r in rows)
        {
            OutputWriter.WriteLine(
                $"{TableText.Truncate(r.DisplayName ?? r.Email ?? "-", 32).PadRight(32)} | " +
                $"{TableText.Truncate(r.PrincipalType ?? "-", 16).PadRight(16)} | " +
                $"{r.Role.PadRight(20)} | {r.PrincipalId}");
        }
    }
#pragma warning restore TXC003
}
