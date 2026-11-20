using TALXIS.Platform.Metadata;
using TALXIS.Platform.Metadata.Serialization.Xml;
using TALXIS.Platform.Metadata.Solutions;

namespace TALXIS.CLI.Platform.Dataverse.Application.Pipeline.Steps;

/// <summary>
/// Normalizes the unpacked export against the local source project via
/// <see cref="ExportNormalizer"/>: server-added system relationships and server-enriched attributes.
/// The environment is the source of truth for solution composition: root components added to or
/// removed from the solution there are synced into the local Solution.xml, which keeps its own
/// layout (bootstrap projects adopt the server manifest once).
/// </summary>
internal sealed class ExportNormalizationStep : ISolutionPullStep
{
    public void Execute(SolutionPullContext context)
    {
        var destinationSolutionXml = Path.Combine(
            context.DestinationDirectory,
            SolutionPullPipelineConstants.OtherDirectoryName,
            "Solution.xml");
        if (!File.Exists(destinationSolutionXml))
            return;

        var reader = new XmlWorkspaceReader();
        var exported = reader.Load(context.StagingDirectory);
        if (exported.Solutions.Count != 1)
            return;

        var source = reader.Load(context.DestinationDirectory);
        var sourceSolution = source.FindSolution(exported.Solutions[0].UniqueName);
        if (sourceSolution is null
            && source.Solutions.Count == 1
            && source.Solutions[0].RootComponents.Count == 0)
        {
            // Fresh clone scaffold: its unique name comes from the output folder, not the solution.
            // Adopt it as the source so bootstrap pulls are still normalized.
            sourceSolution = source.Solutions[0];
            sourceSolution.UniqueName = exported.Solutions[0].UniqueName;
        }

        if (sourceSolution is null)
            return;

        var preserveLocalManifest = sourceSolution.RootComponents.Count > 0;
        var manifestSynced = preserveLocalManifest && SyncRootComponents(exported, sourceSolution, context);

        var options = new ExportNormalizationOptions
        {
            StripComponentsNotInSource = false,
            NormalizeManagedFlag = !preserveLocalManifest,
            NormalizeSolutionVersion = !preserveLocalManifest
        };

        var componentFiles = CaptureComponentFiles(exported);

        var result = new ExportNormalizer().Normalize(exported, source, options);
        if (result.HasChanges)
        {
            new XmlWorkspaceWriter().Write(exported, context.StagingDirectory);

            foreach (var change in result.Changes)
            {
                if (change.ComponentType == ComponentType.EntityRelationship)
                {
                    context.ExcludedRelationships.Add(change.Target);
                    continue;
                }

                if (change.ComponentType is { } componentType
                    && componentFiles.TryGetValue(ComponentFileKey(componentType, change.Target), out var filePath))
                {
                    DeleteComponentFiles(context.StagingDirectory, componentType, filePath);
                }

                context.NormalizationChanges.Add(change.Description);
            }
        }

        if (preserveLocalManifest)
        {
            var stagingSolutionXml = Path.Combine(
                context.StagingDirectory,
                SolutionPullPipelineConstants.OtherDirectoryName,
                "Solution.xml");
            File.Copy(destinationSolutionXml, stagingSolutionXml, overwrite: true);

            if (DeclareDownloadedSubcomponents(exported, sourceSolution, context) > 0 || manifestSynced)
                new XmlWorkspaceWriter().WriteSolutionManifest(source, sourceSolution.UniqueName, context.StagingDirectory);
        }
        else if (DeclareDownloadedSubcomponents(exported, exported.Solutions[0], context) > 0)
        {
            // Bootstrap adopts the server manifest; declare the pulled forms in it right away
            // so a fresh clone does not need a second pull to reach the steady state.
            new XmlWorkspaceWriter().WriteSolutionManifest(exported, exported.Solutions[0].UniqueName, context.StagingDirectory);
        }
    }

    // Runs before normalization so subcomponent rules already see the environment's membership and behavior.
    private static bool SyncRootComponents(Workspace exported, Solution sourceSolution, SolutionPullContext context)
    {
        var serverComponents = exported.Solutions[0].RootComponents
            .Where(rc => rc.Type != ComponentType.SavedQuery)
            .ToArray();
        var changed = false;

        foreach (var serverComponent in serverComponents)
        {
            var local = sourceSolution.RootComponents.FirstOrDefault(rc => SameComponent(rc, serverComponent));
            if (local is null)
            {
                sourceSolution.AddRootComponent(new RootComponent
                {
                    Type = serverComponent.Type,
                    SchemaName = serverComponent.SchemaName,
                    Id = serverComponent.Id,
                    Behavior = serverComponent.Behavior
                });
                context.NormalizationChanges.Add($"Added {Describe(serverComponent)} to the solution manifest: it was added to the solution in the environment.");
                changed = true;
            }
            else if (local.Behavior != serverComponent.Behavior)
            {
                context.NormalizationChanges.Add($"Changed behavior of {Describe(local)} from {local.Behavior} to {serverComponent.Behavior} to match the environment.");
                local.Behavior = serverComponent.Behavior;
                changed = true;
            }
        }

        foreach (var local in sourceSolution.RootComponents.ToArray())
        {
            if (serverComponents.Any(rc => SameComponent(rc, local))) continue;
            // Forms declared on pull for behavior=0 entities are local-only rows; keep them while the form still exists.
            if (local.Type == ComponentType.SystemForm && exported.Forms.Any(f => Guid.TryParse(f.FormId, out var id) && id == local.Id)) continue;

            sourceSolution.RemoveRootComponent(local.Type, local.SchemaName, local.Id);
            context.NormalizationChanges.Add($"Removed {Describe(local)} from the solution manifest: it is no longer in the solution in the environment.");
            changed = true;
        }

        return changed;
    }

    private static bool SameComponent(RootComponent left, RootComponent right) =>
        left.Type == right.Type
        && ((left.Id.HasValue && left.Id == right.Id)
            || (!string.IsNullOrEmpty(left.SchemaName) && string.Equals(left.SchemaName, right.SchemaName, StringComparison.OrdinalIgnoreCase)));

    private static string Describe(RootComponent component) =>
        $"{component.Type} '{component.SchemaName ?? component.Id?.ToString()}'";

    // Forms pulled with a behavior=0 entity become explicit RootComponents in the local manifest,
    // so they survive a later switch of the entity to behavior 1/2. Views are plain subcomponents
    // and never appear in the manifest — they always follow their entity.
    private static int DeclareDownloadedSubcomponents(Workspace exported, Solution sourceSolution, SolutionPullContext context)
    {
        var includedEntities = new HashSet<string>(
            sourceSolution.RootComponents
                .Where(rc => rc.Type == ComponentType.Entity
                    && rc.BehaviorOption == RootComponentBehavior.IncludeSubcomponents
                    && !string.IsNullOrWhiteSpace(rc.SchemaName))
                .Select(rc => rc.SchemaName!),
            StringComparer.OrdinalIgnoreCase);

        var declared = 0;

        foreach (var form in exported.Forms)
        {
            if (form.EntityLogicalName is null || !includedEntities.Contains(form.EntityLogicalName)) continue;
            if (!Guid.TryParse(form.FormId, out var formId)) continue;
            if (sourceSolution.RootComponents.Any(rc => rc.Type == ComponentType.SystemForm && rc.Id == formId)) continue;

            sourceSolution.AddRootComponent(new RootComponent { Type = ComponentType.SystemForm, Id = formId, Behavior = 0 });
            context.NormalizationChanges.Add($"Declared form '{form.DisplayName.Default ?? form.FormId}' of entity '{form.EntityLogicalName}' as a root component.");
            declared++;
        }

        return declared;
    }

    private static Dictionary<string, string> CaptureComponentFiles(Workspace exported)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entity in exported.Entities) Add(files, ComponentType.Entity, entity.LogicalName, entity);
        foreach (var optionSet in exported.GlobalOptionSets) Add(files, ComponentType.OptionSet, optionSet.Name, optionSet);
        foreach (var form in exported.Forms) Add(files, ComponentType.SystemForm, form.FormId, form);
        foreach (var view in exported.Views) Add(files, ComponentType.SavedQuery, view.SavedQueryId, view);
        foreach (var webResource in exported.WebResources) Add(files, ComponentType.WebResource, webResource.WebResourceId, webResource);
        foreach (var workflow in exported.Workflows) Add(files, ComponentType.Workflow, workflow.WorkflowId, workflow);
        foreach (var pluginAssembly in exported.PluginAssemblies) Add(files, ComponentType.PluginAssembly, pluginAssembly.PluginAssemblyId, pluginAssembly);
        foreach (var step in exported.SdkMessageProcessingSteps) Add(files, ComponentType.SdkMessageProcessingStep, step.SdkMessageProcessingStepId, step);
        foreach (var role in exported.SecurityRoles) Add(files, ComponentType.Role, role.RoleId, role);
        foreach (var appModule in exported.AppModules) Add(files, ComponentType.AppModule, appModule.UniqueName, appModule);
        foreach (var siteMap in exported.SiteMaps) Add(files, ComponentType.SiteMap, siteMap.UniqueName, siteMap);
        foreach (var ribbon in exported.Ribbons) Add(files, ComponentType.RibbonCustomization, ribbon.EntityLogicalName, ribbon);

        return files;
    }

    private static void Add(Dictionary<string, string> files, ComponentType type, string? identity, MetadataBase metadata)
    {
        if (identity is null || metadata.Source?.FilePath is not { } filePath) return;
                
        files[ComponentFileKey(type, identity)] = filePath;
    }

    private static string ComponentFileKey(ComponentType type, string identity) => $"{(int)type}:{identity}";

    private static void DeleteComponentFiles(string stagingRoot, ComponentType componentType, string filePath)
    {
        var fullStagingRoot = Path.GetFullPath(stagingRoot);
        var fullFilePath = Path.GetFullPath(filePath);
        if (!fullFilePath.StartsWith(fullStagingRoot, StringComparison.OrdinalIgnoreCase))
            return;

        if (componentType == ComponentType.Entity)
        {
            var entityDirectory = Path.GetDirectoryName(fullFilePath);
            if (entityDirectory is not null && Directory.Exists(entityDirectory))
                Directory.Delete(entityDirectory, recursive: true);
            return;
        }

        DeleteFileIfExists(fullFilePath);
        DeleteFileIfExists(fullFilePath.EndsWith(".data.xml", StringComparison.OrdinalIgnoreCase)
            ? fullFilePath.Substring(0, fullFilePath.Length - ".data.xml".Length)
            : fullFilePath + ".data.xml");
        PruneEmptyDirectories(Path.GetDirectoryName(fullFilePath), fullStagingRoot);
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static void PruneEmptyDirectories(string? directory, string stagingRoot)
    {
        while (directory is not null
            && !string.Equals(Path.GetFullPath(directory), stagingRoot, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(directory)
            && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
            directory = Path.GetDirectoryName(directory);
        }
    }
}
