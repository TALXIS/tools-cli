using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.Team;

/// <summary>
/// Creates a Dataverse team.
/// Usage: <c>txc environment team create --name &lt;name&gt; --type owner|access|aad-security-group|aad-office-group [--aad-object-id &lt;guid&gt;] [--membership-type members-and-guests|members|owners|guests] [--business-unit &lt;name-or-guid&gt;]</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "create",
    Description = "Create a Dataverse team. Types: owner, access, aad-security-group, aad-office-group. AAD-backed types require --aad-object-id and can optionally use --membership-type; owner/access teams must not use those AAD-only options."
)]
public class TeamCreateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(TeamCreateCliCommand));

    [CliOption(Name = "--name", Description = "Team name.", Required = true)]
    public string Name { get; set; } = null!;

    [CliOption(Name = "--type", Description = "Team type: owner, access, aad-security-group, or aad-office-group.", Required = true)]
    public string Type { get; set; } = null!;

    [CliOption(Name = "--aad-object-id", Description = "Required for aad-security-group and aad-office-group teams; must be omitted for owner and access teams.", Required = false)]
    public string? AadObjectId { get; set; }

    [CliOption(Name = "--membership-type", Description = "Optional for aad-security-group and aad-office-group teams: members-and-guests, members, owners, or guests.", Required = false)]
    public string? MembershipType { get; set; }

    [CliOption(Name = "--business-unit", Description = "Business unit name or GUID. Defaults to the caller's business unit.", Required = false)]
    public string? BusinessUnit { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        if (!TeamCommandSupport.TryParseTeamType(Type, Logger, out var teamType))
            return Task.FromResult(ExitValidationError);

        Guid? aadObjectId = null;
        if (!string.IsNullOrWhiteSpace(AadObjectId))
        {
            if (!TeamCommandSupport.TryParseGuidOption(AadObjectId, "--aad-object-id", Logger, out var parsedAadObjectId))
                return Task.FromResult(ExitValidationError);

            aadObjectId = parsedAadObjectId;
        }

        DataverseTeamMembershipType? membershipType = null;
        if (!string.IsNullOrWhiteSpace(MembershipType))
        {
            if (!TeamCommandSupport.TryParseMembershipType(MembershipType, Logger, out var parsedMembershipType))
                return Task.FromResult(ExitValidationError);

            membershipType = parsedMembershipType;
        }

        bool isAadManaged = TeamCommandSupport.IsAadManaged(teamType);
        if (isAadManaged && !aadObjectId.HasValue)
        {
            Logger.LogError("--aad-object-id is required when --type is '{TeamType}'.", TeamCommandSupport.ToCliValue(teamType));
            return Task.FromResult(ExitValidationError);
        }

        if (!isAadManaged && aadObjectId.HasValue)
        {
            Logger.LogError("--aad-object-id is only valid when --type is aad-security-group or aad-office-group.");
            return Task.FromResult(ExitValidationError);
        }

        if (!isAadManaged && membershipType.HasValue)
        {
            Logger.LogError("--membership-type is only valid when --type is aad-security-group or aad-office-group.");
            return Task.FromResult(ExitValidationError);
        }

        var options = new DataverseTeamCreateOptions(Name, teamType, aadObjectId, membershipType, BusinessUnit);
        return ExecuteCreateAsync(options);
    }

    private async Task<int> ExecuteCreateAsync(DataverseTeamCreateOptions options)
    {
        var service = TxcServices.Get<IDataverseTeamService>();

        try
        {
            var team = await service.CreateAsync(Profile, options, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteData(team, TeamCommandSupport.WriteTeamDetail);
            return ExitSuccess;
        }
        catch (DataverseAmbiguousMatchException ex)
        {
            return TeamCommandSupport.HandleDataverseValidationException(Logger, ex, ExitValidationError);
        }
        catch (ArgumentException ex)
        {
            return TeamCommandSupport.HandleDataverseValidationException(Logger, ex, ExitValidationError);
        }
    }
}
