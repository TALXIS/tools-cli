using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC028: <c>[CliReadOnly]</c> commands must not call <c>OutputFormatter.WriteResult()</c>.
/// Read-only commands return data (tables, objects, values) via <c>WriteData</c>,
/// <c>WriteList</c>, or <c>WriteDynamicTable</c>. Using <c>WriteResult</c> produces a
/// <c>CommandResultEnvelope</c> with a status message instead of actual data, confusing
/// both humans and the MCP <c>structuredContent</c> pipeline.
/// Uses semantic analysis to detect actual <c>OutputFormatter.WriteResult</c> invocations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoWriteResultInReadOnlyAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoWriteResultInReadOnly,
        title: "[CliReadOnly] commands must not call OutputFormatter.WriteResult()",
        messageFormat: "'{0}' is a [CliReadOnly] command but calls OutputFormatter.WriteResult(). Use WriteData/WriteList/WriteDynamicTable for data output.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Detect WriteResult invocations inside [CliReadOnly] command types
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.TargetMethod.Name != "WriteResult")
            return;

        var receiverType = invocation.TargetMethod.ContainingType;
        if (receiverType?.Name != "OutputFormatter")
            return;

        // Walk up to find the containing named type
        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType == null)
            return;

        // Must be a [CliReadOnly] command inheriting TxcLeafCommand
        bool isReadOnly = containingType.GetAttributes().Any(a =>
            a.AttributeClass?.Name is "CliReadOnlyAttribute");
        if (!isReadOnly)
            return;

        if (!RoslynHelpers.InheritsFrom(containingType, "TALXIS.CLI.Core.Shared.TxcLeafCommand"))
            return;

        // Report at the invocation site, not the type declaration
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.Syntax.GetLocation(),
            containingType.Name));
    }
}
