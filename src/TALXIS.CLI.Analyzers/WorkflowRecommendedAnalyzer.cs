using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC013: Leaf <c>[CliCommand]</c> classes whose tool name starts with <c>data_</c>
/// should declare <c>[CliWorkflow("...")]</c> to prevent workflow misclassification.
/// The <c>data_</c> prefix is ambiguous — some commands are local-only (data model convert)
/// while others operate on live environments (data package import/export).
/// <para>
/// This analyzer targets commands where the name-based heuristic in <c>ToolCatalog.DeriveWorkflow</c>
/// would default to <c>"local-development"</c> but the command may actually be a live operation.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class WorkflowRecommendedAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.WorkflowRecommended,
        title: "Command with ambiguous name should declare [CliWorkflow]",
        messageFormat: "'{0}' has a 'data_' prefixed tool name but no [CliWorkflow] attribute. The MCP server may misclassify its workflow. Add [CliWorkflow(\"data-operations\")] if it operates on a live environment, or [CliWorkflow(\"local-development\")] if it's local-only.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/TALXIS/tools-cli/blob/main/docs/architecture.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.IsAbstract || type.TypeKind != TypeKind.Class)
            return;

        if (!HasAttribute(type, "CliCommandAttribute"))
            return;

        // Only leaf commands (routing commands are exempt)
        if (IsRoutingCommand(type))
            return;

        if (HasAttribute(type, "McpIgnoreAttribute"))
            return;

        // Already has [CliWorkflow] — no warning needed
        if (HasAttribute(type, "CliWorkflowAttribute"))
            return;

        // Check if this command lives in a namespace that starts with "Data" 
        // (the data_ prefix in tool names comes from the CLI hierarchy)
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        var className = type.Name;

        // Target: classes in TALXIS.CLI.Features.Data namespace (top-level data commands)
        // These are the ambiguous ones — data_model_convert is local, data_package_import is live
        if (ns.Contains("TALXIS.CLI.Features.Data") && !ns.Contains("Environment"))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], type.Name));
        }
    }

    private static bool HasAttribute(INamedTypeSymbol type, string attributeName)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.Name == attributeName)
                return true;
        }
        return false;
    }

    private static bool IsRoutingCommand(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers("Run"))
        {
            if (member is IMethodSymbol method
                && method.ReturnsVoid
                && method.Parameters.Length == 1
                && method.Parameters[0].Type.Name == "CliContext")
            {
                return true;
            }
        }
        return false;
    }
}
