using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Role;

/// <summary>
/// Gets one Dataverse security role by role name or GUID so you can confirm the
/// exact value to pass to <c>--role</c> on other <c>txc environment</c> commands.
/// Usage: <c>txc environment role get --role &lt;name-or-guid&gt;</c>
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get a Dataverse security role by role name or GUID."
)]
#pragma warning disable TXC003
public class RoleGetCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(RoleGetCliCommand));

    [CliOption(Name = "--role", Description = "Role name or GUID to resolve for use with other --role options.", Required = true)]
    public string Role { get; set; } = null!;

    protected override Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(Role))
        {
            Logger.LogError("Specify --role with a role name or GUID.");
            return Task.FromResult(ExitValidationError);
        }

        return ExecuteGetRoleAsync(Role.Trim());
    }

    private async Task<int> ExecuteGetRoleAsync(string role)
    {
        var service = TxcServices.Get<IDataverseRoleService>();

        try
        {
            var row = await service.GetAsync(Profile, role, CancellationToken.None).ConfigureAwait(false);
            if (row is null)
            {
                Logger.LogError("Role '{Role}' not found.", role);
                return ExitValidationError;
            }

            OutputFormatter.WriteData(row, PrintRole);
            return ExitSuccess;
        }
        catch (DataverseAmbiguousMatchException ex)
        {
            Logger.LogError("Multiple roles matched '{Role}'. Specify the role GUID instead.", role);
            foreach (var candidate in ex.Candidates)
            {
                Logger.LogError(
                    "Candidate: {Name} | Business Unit: {BusinessUnit} | Id: {Id}",
                    candidate.Name,
                    candidate.Description ?? "-",
                    candidate.Id);
            }

            return ExitValidationError;
        }
    }

    private static void PrintRole(DataverseRoleRecord role)
    {
        OutputWriter.WriteLine($"Name:          {role.Name}");
        OutputWriter.WriteLine($"Business Unit: {role.BusinessUnitName ?? "-"}");
        OutputWriter.WriteLine($"Id:            {role.Id}");
    }
}
#pragma warning restore TXC003
