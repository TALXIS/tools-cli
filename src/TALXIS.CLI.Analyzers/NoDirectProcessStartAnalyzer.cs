using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC026: Command code must not spawn processes directly via <c>Process.Start()</c>,
/// <c>new Process()</c>, or <c>new ProcessStartInfo()</c>.
/// Commands should use <c>CliSubprocessRunner</c> for subprocess dispatch with trace
/// propagation, output capture, and log forwarding. Direct process spawning bypasses
/// telemetry correlation and the MCP output pipeline.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoDirectProcessStartAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoDirectProcessStart,
        title: "Do not spawn processes directly in command code — use CliSubprocessRunner",
        messageFormat: "'{0}' directly uses {1}. Use CliSubprocessRunner or dedicated infrastructure instead.",
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
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        var type = creation.Type;
        if (type?.ContainingNamespace?.ToDisplayString() != "System.Diagnostics")
            return;

        if (type.Name is not ("Process" or "ProcessStartInfo"))
            return;

        if (!IsInCommandClass(context.ContainingSymbol))
            return;

        var containingTypeName = context.ContainingSymbol?.ContainingType?.Name ?? "Unknown";
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            creation.Syntax.GetLocation(),
            containingTypeName,
            $"new {type.Name}()"));
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method.Name != "Start")
            return;

        var type = method.ContainingType;
        if (type?.Name != "Process" || type.ContainingNamespace?.ToDisplayString() != "System.Diagnostics")
            return;

        if (!IsInCommandClass(context.ContainingSymbol))
            return;

        var containingTypeName = context.ContainingSymbol?.ContainingType?.Name ?? "Unknown";
        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.Syntax.GetLocation(),
            containingTypeName,
            "Process.Start()"));
    }

    private static bool IsInCommandClass(ISymbol? symbol)
    {
        var type = symbol?.ContainingType;
        if (type == null) return false;

        return type.GetAttributes().Any(a => a.AttributeClass?.Name is "CliCommandAttribute")
            || RoslynHelpers.InheritsFrom(type, "TALXIS.CLI.Core.Shared.TxcLeafCommand");
    }
}
