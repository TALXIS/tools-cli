using TALXIS.Platform.Metadata;
using TALXIS.Platform.Metadata.Serialization.Xml;
using TALXIS.Platform.Metadata.Solutions;

namespace TALXIS.CLI.Platform.Dataverse.Application.Pipeline.Steps;

/// <summary>
/// Normalizes the unpacked export against the local source project via
/// <see cref="ExportNormalizer"/>: server-added system relationships, components outside
/// the source solution, and server-enriched attributes.
/// The local Solution.xml is the source of truth for solution composition: once it declares
/// root components, pull never overwrites it (bootstrap projects adopt the server manifest once).
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
        if (sourceSolution is null)
            return;

        var preserveLocalManifest = sourceSolution.RootComponents.Count > 0;
        var options = new ExportNormalizationOptions
        {
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

            if (DeclareDownloadedSubcomponents(exported, sourceSolution, context) > 0)
                new XmlWorkspaceWriter().WriteSolutionManifest(source, sourceSolution.UniqueName, context.StagingDirectory);
        }
    }

    // Subcomponents pulled with a behavior=0 entity become explicit RootComponents in the local
    // manifest, so they survive a later switch of the entity to behavior 1/2.
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

        foreach (var view in exported.Views)
        {
            if (view.EntityLogicalName is null || !includedEntities.Contains(view.EntityLogicalName)) continue;
            if (!Guid.TryParse(view.SavedQueryId, out var viewId)) continue;
            if (sourceSolution.RootComponents.Any(rc => rc.Type == ComponentType.SavedQuery && rc.Id == viewId)) continue;

            sourceSolution.AddRootComponent(new RootComponent { Type = ComponentType.SavedQuery, Id = viewId, Behavior = 0 });
            context.NormalizationChanges.Add($"Declared view '{view.DisplayName.Default ?? view.SavedQueryId}' of entity '{view.EntityLogicalName}' as a root component.");
            declared++;
        }

        return declared;
    }

    private static Dictionary<string, string> CaptureComponentFiles(Workspace exported)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(ComponentType type, string? identity, MetadataBase metadata)
        {
            if (identity is null || metadata.Source?.FilePath is not { } filePath)
                return;
            files[ComponentFileKey(type, identity)] = filePath;
        }

        foreach (var entity in exported.Entities) Add(ComponentType.Entity, entity.LogicalName, entity);
        foreach (var optionSet in exported.GlobalOptionSets) Add(ComponentType.OptionSet, optionSet.Name, optionSet);
        foreach (var form in exported.Forms) Add(ComponentType.SystemForm, form.FormId, form);
        foreach (var view in exported.Views) Add(ComponentType.SavedQuery, view.SavedQueryId, view);
        foreach (var webResource in exported.WebResources) Add(ComponentType.WebResource, webResource.WebResourceId, webResource);
        foreach (var workflow in exported.Workflows) Add(ComponentType.Workflow, workflow.WorkflowId, workflow);
        foreach (var pluginAssembly in exported.PluginAssemblies) Add(ComponentType.PluginAssembly, pluginAssembly.PluginAssemblyId, pluginAssembly);
        foreach (var step in exported.SdkMessageProcessingSteps) Add(ComponentType.SdkMessageProcessingStep, step.SdkMessageProcessingStepId, step);
        foreach (var role in exported.SecurityRoles) Add(ComponentType.Role, role.RoleId, role);
        foreach (var appModule in exported.AppModules) Add(ComponentType.AppModule, appModule.UniqueName, appModule);
        foreach (var siteMap in exported.SiteMaps) Add(ComponentType.SiteMap, siteMap.UniqueName, siteMap);
        foreach (var ribbon in exported.Ribbons) Add(ComponentType.RibbonCustomization, ribbon.EntityLogicalName, ribbon);

        return files;
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
