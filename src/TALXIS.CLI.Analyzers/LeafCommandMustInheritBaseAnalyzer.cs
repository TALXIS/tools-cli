using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC001: Every non-abstract class with <c>[CliCommand]</c> that is a leaf command
/// (does not have a <c>void Run(CliContext)</c> routing method) must inherit from
/// <c>TxcLeafCommand</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LeafCommandMustInheritBaseAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.MustInheritTxcLeafCommand,
        title: "Leaf CLI command must inherit TxcLeafCommand",
        messageFormat: "'{0}' has [CliCommand] but does not inherit TxcLeafCommand. Leaf commands must extend TxcLeafCommand (or ProfiledCliCommand) to ensure consistent output formatting and error handling.",
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

        // Only concrete classes
        if (type.IsAbstract || type.TypeKind != TypeKind.Class)
            return;

        // Must have [CliCommand] attribute
        if (!HasCliCommandAttribute(type))
            return;

        // Skip routing/hub commands — they have void Run(CliContext)
        if (IsRoutingCommand(type))
            return;

        // Must inherit TxcLeafCommand
        if (RoslynHelpers.InheritsFrom(type, "TALXIS.CLI.Core.TxcLeafCommand"))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], type.Name));
    }

    private static bool HasCliCommandAttribute(INamedTypeSymbol type)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.Name == "CliCommandAttribute")
                return true;
        }
        return false;
    }

    private static bool IsRoutingCommand(INamedTypeSymbol type)
    {
        // A routing command has a void Run(CliContext) method
        foreach (var member in type.GetMembers("Run"))
        {
            if (member is IMethodSymbol method
                && method.ReturnsVoid
                && method.Parameters.Length == 1
                && method.Parameters[0].Type.Name == "CliContext")
            {
                return true;
            }
        }
        return false;
    }

}
