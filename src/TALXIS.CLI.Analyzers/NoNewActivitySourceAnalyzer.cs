using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC017: <c>new ActivitySource(…)</c> must only appear in the canonical
/// <c>TxcActivitySource</c> class (in <c>TALXIS.CLI.Abstractions</c>).
/// Creating additional instances causes duplicate trace sources and risks
/// version/name drift between assemblies.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoNewActivitySourceAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoNewActivitySource,
        title: "Do not create new ActivitySource instances — use TxcActivitySource.Instance",
        messageFormat: "'{0}' creates a new ActivitySource. Use TxcActivitySource.Instance from TALXIS.CLI.Abstractions instead.",
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

        if (type?.Name != "ActivitySource")
            return;

        if (type.ContainingNamespace?.ToDisplayString() != "System.Diagnostics")
            return;

        // Allow inside TxcActivitySource itself — that's the canonical factory.
        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType?.Name == "TxcActivitySource")
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
