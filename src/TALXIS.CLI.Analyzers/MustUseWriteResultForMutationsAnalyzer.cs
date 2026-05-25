using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC027: Mutative commands (<c>[CliDestructive]</c> or <c>[CliIdempotent]</c>) must call
/// <c>OutputFormatter.WriteResult()</c> to produce a <c>CommandResultEnvelope</c>.
/// Without <c>WriteResult</c>, the MCP server cannot detect the envelope and build
/// proper <c>content</c>/<c>structuredContent</c> for the client.
/// Uses semantic analysis to detect actual <c>OutputFormatter.WriteResult</c> invocations.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustUseWriteResultForMutationsAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.MustUseWriteResultForMutations,
        title: "Mutative commands must call OutputFormatter.WriteResult()",
        messageFormat: "'{0}' is a mutative command but does not call OutputFormatter.WriteResult(). Mutative commands must produce a CommandResultEnvelope so the MCP server can build consistent responses.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            // Track which mutative command types call WriteResult
            var typesWithWriteResult = new ConcurrentDictionary<INamedTypeSymbol, bool>(SymbolEqualityComparer.Default);

            // Detect WriteResult invocations semantically
            compilationContext.RegisterOperationAction(operationContext =>
            {
                var invocation = (IInvocationOperation)operationContext.Operation;
                if (invocation.TargetMethod.Name != "WriteResult")
                    return;

                var receiverType = invocation.TargetMethod.ContainingType;
                if (receiverType?.Name != "OutputFormatter")
                    return;

                // Walk up to find the containing named type
                var containingType = operationContext.ContainingSymbol?.ContainingType;
                if (containingType != null)
                    typesWithWriteResult[containingType] = true;
            }, OperationKind.Invocation);

            // At end of compilation, check mutative types that never called WriteResult
            compilationContext.RegisterSymbolAction(symbolContext =>
            {
                var type = (INamedTypeSymbol)symbolContext.Symbol;
                if (type.IsAbstract || type.TypeKind != TypeKind.Class)
                    return;

                bool isMutative = type.GetAttributes().Any(a =>
                    a.AttributeClass?.Name is "CliDestructiveAttribute" or "CliIdempotentAttribute");
                if (!isMutative)
                    return;

                if (!RoslynHelpers.InheritsFrom(type, "TALXIS.CLI.Core.Shared.TxcLeafCommand"))
                    return;

                if (typesWithWriteResult.ContainsKey(type))
                    return;

                symbolContext.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    type.Locations.FirstOrDefault(),
                    type.Name));
            }, SymbolKind.NamedType);
        });
    }
}
