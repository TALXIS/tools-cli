using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Environment.User;

/// <summary>
/// Grants the current authenticated caller Dataverse admin access in the selected environment.
/// Usage: <c>txc environment user self-elevate</c>
/// </summary>
[CliIdempotent]
[CliCommand(
    Name = "self-elevate",
    Description = "Grant the current authenticated caller the environment admin role in the selected environment. To add other users to this environment, use 'environment user add' instead."
)]
#pragma warning disable TXC003
public class UserSelfElevateCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(UserSelfElevateCliCommand));

    protected override async Task<int> ExecuteAsync()
    {
        var resolver = TxcServices.Get<TALXIS.CLI.Core.Abstractions.IConfigurationResolver>();
        var context = await resolver.ResolveAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var environmentId = await UserCliCommandSupport.ResolveEnvironmentIdAsync(context, CancellationToken.None).ConfigureAwait(false);

        await UserCliCommandSupport.SelfElevateAsync(context, environmentId, CancellationToken.None).ConfigureAwait(false);

        OutputFormatter.WriteData(
            new
            {
                status = "elevated",
                environmentId,
                caller = "current",
            },
            _ => OutputWriter.WriteLine("Environment admin role applied to the current authenticated caller."));

        return ExitSuccess;
    }
}
