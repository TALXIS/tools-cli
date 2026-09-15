using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC029: Do not construct <c>new CallToolResult</c> directly — use
/// <c>McpToolResultFactory</c> to ensure <c>content</c>/<c>structuredContent</c> consistency.
/// The factory guarantees both surfaces carry the same information, preventing
/// mismatches where <c>content</c> says one thing and <c>structuredContent</c> says another.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoDirectCallToolResultAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoDirectCallToolResult,
        title: "Do not construct CallToolResult directly — use McpToolResultFactory",
        messageFormat: "'{0}' constructs CallToolResult directly. Use McpToolResultFactory methods to ensure content/structuredContent consistency.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeObjectCreation, OperationKind.ObjectCreation);
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        var type = creation.Type;

        if (type?.Name != "CallToolResult")
            return;

        // Only target the MCP protocol type
        if (type.ContainingNamespace?.ToDisplayString() != "ModelContextProtocol.Protocol")
            return;

        // Allow inside McpToolResultFactory — it IS the factory
        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType?.Name == "McpToolResultFactory")
            return;

        var containingTypeName = containingType?.Name
            ?? context.ContainingSymbol?.Name
            ?? "Unknown";

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            creation.Syntax.GetLocation(),
            containingTypeName));
    }
}
