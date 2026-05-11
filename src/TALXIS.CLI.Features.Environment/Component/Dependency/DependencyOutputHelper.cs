using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.Platform.Metadata;

namespace TALXIS.CLI.Features.Environment.Component.Dependency;

/// <summary>
/// Shared text-rendering helpers for dependency command output.
/// </summary>
internal static class DependencyOutputHelper
{
    // OutputWriter usage is intentional — called from text-renderer callbacks.
#pragma warning disable TXC003
    public static void PrintDependencyTable(
        IReadOnlyList<DependencyRow> rows,
        string primaryLabel,
        string secondaryLabel,
        string? headerMessage = null)
    {
        if (rows.Count == 0)
        {
            OutputWriter.WriteLine("No dependencies found.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(headerMessage))
            OutputWriter.WriteLine(headerMessage + "\n");

        string header = $"{primaryLabel + " Type",-25} | {primaryLabel + " ID",-36} | {secondaryLabel + " Type",-25} | {secondaryLabel + " ID",-36} | Dep.Type";
        OutputWriter.WriteLine(header);
        OutputWriter.WriteLine(new string('-', header.Length));

        foreach (var d in rows)
        {
            var depType = ComponentDefinitionRegistry.GetByType((ComponentType)d.DependentComponentType)?.Name ?? d.DependentComponentType.ToString();
            var reqType = ComponentDefinitionRegistry.GetByType((ComponentType)d.RequiredComponentType)?.Name ?? d.RequiredComponentType.ToString();
            var depKind = d.DependencyType switch
            {
                1 => "Internal",
                2 => "Published",
                4 => "Unpublished",
                _ => d.DependencyType.ToString(),
            };

            // Primary/secondary swap depending on which perspective the caller wants
            var (priType, priId, secType, secId) = primaryLabel == "Dependent"
                ? (depType, d.DependentComponentId, reqType, d.RequiredComponentId)
                : (reqType, d.RequiredComponentId, depType, d.DependentComponentId);

            OutputWriter.WriteLine($"{priType,-25} | {priId,-36} | {secType,-25} | {secId,-36} | {depKind}");
        }

        OutputWriter.WriteLine($"\n{rows.Count} dependency(ies) found.");
    }
#pragma warning restore TXC003
}
