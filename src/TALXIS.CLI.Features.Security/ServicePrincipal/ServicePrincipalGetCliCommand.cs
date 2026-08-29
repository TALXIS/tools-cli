using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Features.Security;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Features.Security.ServicePrincipal;

[CliReadOnly]
[CliCommand(
    Name = "get",
    Description = "Get one Entra application by client ID, object ID, or exact display name when no environment is resolved. When --environment is provided or the active connection already targets an environment, get the Dataverse service principal by system-user GUID or application client ID instead."
)]
public class ServicePrincipalGetCliCommand : SecurityScopedCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger(nameof(ServicePrincipalGetCliCommand));

    [CliOption(Name = "--service-principal", Description = "Application client ID, service principal object ID, or exact display name. With an environment scope, a Dataverse system-user GUID is also accepted.", Required = true)]
    public string ServicePrincipal { get; set; } = null!;

    protected override async Task<int> ExecuteAsync()
    {
        var scope = await SecurityPrincipalCommandSupport.ResolveScopeAsync(Profile, Environment, CancellationToken.None).ConfigureAwait(false);
        return scope.HasEnvironment
            ? await ExecuteEnvironmentGetAsync(scope).ConfigureAwait(false)
            : await ExecuteTenantGetAsync().ConfigureAwait(false);
    }

    private async Task<int> ExecuteEnvironmentGetAsync(SecurityScopeContext scope)
    {
        try
        {
            var service = TxcServices.Get<IDataverseServicePrincipalService>();
            var app = await service.GetAsync(Profile, ServicePrincipal, CancellationToken.None, scope.EnvironmentId).ConfigureAwait(false);
            if (app is null)
            {
                Logger.LogError("Service principal '{ServicePrincipal}' not found.", ServicePrincipal);
                return ExitValidationError;
            }

            OutputFormatter.WriteData(app, ServicePrincipalCommandSupport.WriteServicePrincipalDetails);
            return ExitSuccess;
        }
        catch (Exception ex) when (SecurityPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }

    private async Task<int> ExecuteTenantGetAsync()
    {
        try
        {
            var app = await ServicePrincipalCommandSupport.GetServicePrincipalAsync(Profile, ServicePrincipal, CancellationToken.None).ConfigureAwait(false);
            OutputFormatter.WriteData(app, ServicePrincipalCommandSupport.WriteServicePrincipalDetail);
            return ExitSuccess;
        }
        catch (Exception ex) when (SecurityPrincipalCommandSupport.TryHandleValidationException(Logger, ex, out var exitCode))
        {
            return exitCode;
        }
    }
}
