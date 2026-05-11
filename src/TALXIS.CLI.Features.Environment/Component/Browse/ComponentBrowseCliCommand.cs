using DotMake.CommandLine;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Logging;
using TALXIS.Platform.Metadata;

namespace TALXIS.CLI.Features.Environment.Component.Browse;

/// <summary>
/// Opens the Power Platform web editor for a component instance.
/// Resolves the appropriate URL based on component type and opens it in the default browser.
/// In headless mode, only prints the URL without opening the browser.
/// </summary>
[CliReadOnly]
[CliCommand(
    Name = "browse",
    Description = "Open the web editor for a component in the connected live environment. Requires an active profile."
)]
public class ComponentBrowseCliCommand : ProfiledCliCommand
{
    protected override ILogger Logger { get; } = TxcLoggerFactory.CreateLogger<ComponentBrowseCliCommand>();

    [CliOption(Name = "--type", Description = "Component type (name, alias, or integer code). Run 'txc component type list' to see available types.", Required = true)]
    public string Type { get; set; } = null!;

    [CliOption(Name = "--id", Description = "Component GUID. Mutually exclusive with --name.", Required = false)]
    public string? Id { get; set; }

    [CliOption(Name = "--name", Description = "Component friendly name (resolved to GUID). Mutually exclusive with --id. Supported for: solution (unique name), entity (logical name).", Required = false)]
    public string? Name { get; set; }

    [CliOption(Name = "--entity", Description = "Entity logical name. Required for form/view types. Also provides the backing entity name for SCF types.", Required = false)]
    public string? Entity { get; set; }

    [CliOption(Name = "--solution", Description = "Solution unique name for solution-scoped URLs. Resolved to GUID.", Required = false)]
    public string? Solution { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        // Validate --id / --name mutual exclusion
        if (string.IsNullOrWhiteSpace(Id) && string.IsNullOrWhiteSpace(Name))
        {
            Logger.LogError("Provide --id <guid> or --name <friendly-name>.");
            return ExitValidationError;
        }
        if (!string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(Name))
        {
            Logger.LogError("--id and --name are mutually exclusive. Provide one, not both.");
            return ExitValidationError;
        }

        // Resolve component type
        var def = ComponentDefinitionRegistry.GetByName(Type);
        ComponentType? typeCode = def?.TypeCode;

        // Allow raw integer codes even without a registered definition
        if (typeCode is null && int.TryParse(Type, out var rawCode))
            typeCode = (ComponentType)rawCode;

        if (typeCode is null)
        {
            Logger.LogError("Unknown component type '{Type}'. Run 'txc component type list' to see available types.", Type);
            return ExitValidationError;
        }

        // Resolve the component GUID
        Guid componentId;
        if (!string.IsNullOrWhiteSpace(Id))
        {
            if (!Guid.TryParse(Id, out componentId))
            {
                Logger.LogError("Invalid GUID: '{Id}'.", Id);
                return ExitValidationError;
            }
        }
        else
        {
            // --name resolution depends on type
            var resolved = await ResolveNameToGuidAsync(typeCode.Value, Name!).ConfigureAwait(false);
            if (resolved is null)
                return ExitValidationError;
            componentId = resolved.Value;
        }

        // Resolve environment ID from profile connection
        var resolver = TxcServices.Get<IConfigurationResolver>();
        var context = await resolver.ResolveAsync(Profile, CancellationToken.None).ConfigureAwait(false);
        var connection = context.Connection;

        if (connection.EnvironmentId is null)
        {
            Logger.LogError("Environment ID is not set on the connection. Run 'txc config connection check' to populate it.");
            return ExitValidationError;
        }
        var environmentId = connection.EnvironmentId.Value;

        // Resolve --solution if provided
        Guid? solutionId = null;
        if (!string.IsNullOrWhiteSpace(Solution))
        {
            var slnService = TxcServices.Get<ISolutionDetailService>();
            var (sln, _) = await slnService.ShowAsync(Profile, Solution, CancellationToken.None).ConfigureAwait(false);
            solutionId = sln.Id;
        }

        // Validate type-specific requirements
        if (typeCode is ComponentType.SystemForm or ComponentType.Form or ComponentType.SavedQuery
            && string.IsNullOrWhiteSpace(Entity))
        {
            Logger.LogError("--entity is required for form/view types.");
            return ExitValidationError;
        }

        // Build URL
        var orgUrl = connection.EnvironmentUrl?.Replace("https://", "").TrimEnd('/');
        var url = MakerPortalUrlBuilder.Build(environmentId, orgUrl, typeCode.Value, componentId, Entity, solutionId);

        if (url is null)
        {
            Logger.LogError(
                "Cannot build URL for type '{Type}' (code {Code}). For SCF types, provide --entity (backing entity logical name).",
                Type, (int)typeCode.Value);
            return ExitValidationError;
        }

        // Output URL and open browser
        OutputFormatter.WriteData(new { url = url.AbsoluteUri, type = def?.Name ?? typeCode.Value.ToString(), componentId },
            _ =>
            {
                OutputWriter.WriteLine(url.AbsoluteUri);
            });

        BrowserLauncher.Open(url, Logger);
        return ExitSuccess;
    }

    private async Task<Guid?> ResolveNameToGuidAsync(ComponentType typeCode, string name)
    {
        switch (typeCode)
        {
            case ComponentType.Solution:
                var slnService = TxcServices.Get<ISolutionDetailService>();
                var (sln, _) = await slnService.ShowAsync(Profile, name, CancellationToken.None).ConfigureAwait(false);
                return sln.Id;

            case ComponentType.Entity:
                var metadataResolver = TxcServices.Get<IMetadataIdResolver>();
                var entityId = await metadataResolver.ResolveEntityIdAsync(Profile, name, CancellationToken.None).ConfigureAwait(false);
                return entityId;

            default:
                Logger.LogError("--name is not supported for type '{Type}'. Use --id <guid> instead.", Type);
                return null;
        }
    }
}
