using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC018: <c>[CliCommand]</c> classes should not call
/// <c>Environment.GetEnvironmentVariable</c> directly. Configuration should
/// flow through DI, CLI options, or the <c>GlobalConfig</c> model — not raw
/// env-var reads scattered across command implementations.
/// <para>
/// Infrastructure code (bootstrap, telemetry setup, session resolvers) is
/// exempt — suppress with <c>#pragma warning disable TXC018</c> when
/// env-var access is truly needed at that layer.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoEnvVarInCommandsAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoEnvVarInCommands,
        title: "Command classes should not read environment variables directly",
        messageFormat: "'{0}' calls Environment.GetEnvironmentVariable. Use DI, CLI options, or GlobalConfig instead.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        if (method.Name != "GetEnvironmentVariable")
            return;

        if (method.ContainingType?.Name != "Environment"
            || method.ContainingType.ContainingNamespace?.ToDisplayString() != "System")
            return;

        // Only flag inside [CliCommand]-attributed classes
        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType == null || !HasCliCommandAttribute(containingType))
            return;

        var containingTypeName = containingType.Name;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.Syntax.GetLocation(),
            containingTypeName));
    }

    private static bool HasCliCommandAttribute(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.Name is "CliCommandAttribute" or "CliCommand")
                return true;
        }

        // Check base types — TxcLeafCommand itself doesn't have [CliCommand],
        // but the concrete command subclass does.
        return false;
    }
}
