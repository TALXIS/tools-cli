using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC008: In classes inheriting <c>TxcLeafCommand</c>, flag <c>ILogger</c>-typed
/// private or protected fields. The base class declares <c>protected abstract ILogger Logger { get; }</c>
/// — subclasses must override that property, not shadow it with a field.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustOverrideLoggerAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.MustOverrideLogger,
        title: "Override the Logger property instead of declaring an ILogger field",
        messageFormat: "'{0}' declares an ILogger field '{1}'. Override the inherited 'protected abstract ILogger Logger {{ get; }}' property from TxcLeafCommand instead.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/TALXIS/tools-cli/blob/main/docs/output-contract.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;

        // Only check private/protected fields
        if (field.DeclaredAccessibility != Accessibility.Private
            && field.DeclaredAccessibility != Accessibility.Protected
            && field.DeclaredAccessibility != Accessibility.ProtectedOrInternal)
            return;

        // Check if the field type is ILogger (any generic variant)
        if (field.Type.Name != "ILogger")
            return;

        // Check if it's from the Microsoft.Extensions.Logging namespace
        if (field.Type.ContainingNamespace?.ToDisplayString() != "Microsoft.Extensions.Logging")
            return;

        // The containing type must inherit TxcLeafCommand
        var containingType = field.ContainingType;
        if (containingType == null || !RoslynHelpers.InheritsFrom(containingType, "TALXIS.CLI.Core.TxcLeafCommand"))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            field.Locations[0],
            containingType.Name,
            field.Name));
    }
}
