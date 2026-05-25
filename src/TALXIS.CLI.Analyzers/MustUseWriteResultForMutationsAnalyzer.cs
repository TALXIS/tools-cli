using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC027: Mutative commands (<c>[CliDestructive]</c> or <c>[CliIdempotent]</c>) must call
/// <c>OutputFormatter.WriteResult()</c> to produce a <c>CommandResultEnvelope</c>.
/// Without <c>WriteResult</c>, the MCP server cannot detect the envelope and build
/// proper <c>content</c>/<c>structuredContent</c> for the client.
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
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.IsAbstract || type.TypeKind != TypeKind.Class)
            return;

        // Must be a mutative command ([CliDestructive] or [CliIdempotent])
        bool isMutative = type.GetAttributes().Any(a =>
            a.AttributeClass?.Name is "CliDestructiveAttribute" or "CliIdempotentAttribute");
        if (!isMutative)
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

        // Check all syntax references for WriteResult invocations
        foreach (var syntaxRef in executeAsync.DeclaringSyntaxReferences)
        {
            var syntax = syntaxRef.GetSyntax(context.CancellationToken);
            var text = syntax.ToFullString();
            // Simple text-based check for WriteResult presence.
            // A full semantic check would require a SyntaxNodeAction + SemanticModel
            // which is significantly more complex. This is sufficient since
            // OutputFormatter.WriteResult is a distinctive call site.
            if (text.Contains("WriteResult"))
                return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            type.Locations.FirstOrDefault(),
            type.Name));
    }
}
