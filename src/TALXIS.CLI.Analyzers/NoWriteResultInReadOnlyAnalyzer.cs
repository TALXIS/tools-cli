using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC028: <c>[CliReadOnly]</c> commands must not call <c>OutputFormatter.WriteResult()</c>.
/// Read-only commands return data (tables, objects, values) via <c>WriteData</c>,
/// <c>WriteList</c>, or <c>WriteDynamicTable</c>. Using <c>WriteResult</c> produces a
/// <c>CommandResultEnvelope</c> with a status message instead of actual data, confusing
/// both humans and the MCP <c>structuredContent</c> pipeline.
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
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.IsAbstract || type.TypeKind != TypeKind.Class)
            return;

        // Must be a [CliReadOnly] command
        bool isReadOnly = type.GetAttributes().Any(a =>
            a.AttributeClass?.Name is "CliReadOnlyAttribute");
        if (!isReadOnly)
            return;

        // Must inherit TxcLeafCommand
        if (!RoslynHelpers.InheritsFrom(type, "TALXIS.CLI.Core.Shared.TxcLeafCommand"))
            return;

        // Look for WriteResult call in ExecuteAsync method body
        var executeAsync = type.GetMembers("ExecuteAsync")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(m => m.IsOverride || m.DeclaredAccessibility == Accessibility.Protected);
        if (executeAsync == null)
            return;

        foreach (var syntaxRef in executeAsync.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax(context.CancellationToken);
            var text = syntax.ToFullString();
            if (text.Contains("WriteResult"))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    type.Locations.FirstOrDefault(),
                    type.Name));
                return;
            }
        }
    }
}
