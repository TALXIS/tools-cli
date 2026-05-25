using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC022: Methods named <c>GetInnermostException</c> must only be defined in the
/// shared <c>ExceptionHelpers</c> class (in <c>TALXIS.CLI.Abstractions</c>).
/// Duplicate helper methods lead to divergent behaviour when one copy is updated
/// but the other is forgotten.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoDuplicateExceptionHelperAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoDuplicateExceptionHelper,
        title: "GetInnermostException must only be defined in ExceptionHelpers",
        messageFormat: "'{0}' defines GetInnermostException. Use ExceptionHelpers.GetInnermostException from TALXIS.CLI.Abstractions instead.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
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

        if (method.Name != "GetInnermostException")
            return;

        // Allow the canonical definition in ExceptionHelpers
        if (method.ContainingType?.Name == "ExceptionHelpers")
            return;

        var containingTypeName = method.ContainingType?.Name ?? "Unknown";

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            method.Locations.FirstOrDefault(),
            containingTypeName));
    }
}
