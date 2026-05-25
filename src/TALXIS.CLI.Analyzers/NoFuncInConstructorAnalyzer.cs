using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC023: Public class constructors should prefer named interfaces over
/// <c>Func&lt;…&gt;</c> or <c>Action&lt;…&gt;</c> parameters. Anonymous delegates
/// make constructor signatures opaque — callers and DI containers cannot
/// distinguish between two <c>Func&lt;string?&gt;</c> parameters without reading
/// the parameter names. A small named interface (or <c>record</c>) is self-documenting.
/// <para>
/// Severity is <c>Info</c> because <c>Func&lt;&gt;</c> injection is occasionally
/// the pragmatic choice (e.g., deferred evaluation of a value that is not yet
/// available at DI registration time).
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoFuncInConstructorAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoFuncInConstructor,
        title: "Prefer named interfaces over Func<>/Action<> parameters in constructors",
        messageFormat: "Constructor of '{0}' has a {1} parameter '{2}'. Consider replacing with a named interface for clarity.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        if (method.MethodKind != MethodKind.Constructor)
            return;

        // Only flag public constructors of public classes
        if (method.DeclaredAccessibility != Accessibility.Public)
            return;
        if (method.ContainingType?.DeclaredAccessibility != Accessibility.Public)
            return;

        foreach (var parameter in method.Parameters)
        {
            if (parameter.Type is not INamedTypeSymbol paramType)
                continue;

            var typeName = paramType.Name;
            if (typeName is not ("Func" or "Action"))
                continue;

            var ns = paramType.ContainingNamespace?.ToDisplayString();
            if (ns != "System")
                continue;

            var containingTypeName = method.ContainingType.Name;

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                parameter.Locations.FirstOrDefault(),
                containingTypeName,
                paramType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                parameter.Name));
        }
    }
}
