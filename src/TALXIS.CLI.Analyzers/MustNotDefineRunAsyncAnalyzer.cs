using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC002: Leaf commands that inherit <c>TxcLeafCommand</c> must not define their
/// own <c>RunAsync()</c> method — the base class owns it and provides standardized
/// error handling. Implement <c>ExecuteAsync()</c> instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotDefineRunAsyncAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.MustNotDefineRunAsync,
        title: "Leaf commands must not define RunAsync()",
        messageFormat: "'{0}' defines RunAsync() but inherits TxcLeafCommand which owns this method. Implement ExecuteAsync() instead.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/TALXIS/tools-cli/blob/main/docs/output-contract.md");

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

        // Only check classes that inherit TxcLeafCommand
        if (!InheritsFrom(type, "TALXIS.CLI.Core.TxcLeafCommand"))
            return;

        // Check for RunAsync() declared directly on this type (not inherited)
        foreach (var member in type.GetMembers("RunAsync"))
        {
            if (member is IMethodSymbol method
                && method.DeclaredAccessibility == Accessibility.Public
                && method.Parameters.Length == 0
                && method.ContainingType.Equals(type, SymbolEqualityComparer.Default))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, method.Locations[0], type.Name));
            }
        }
    }

    private static bool InheritsFrom(INamedTypeSymbol type, string fullName)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (current.ToDisplayString() == fullName)
                return true;
            current = current.BaseType;
        }
        return false;
    }
}
