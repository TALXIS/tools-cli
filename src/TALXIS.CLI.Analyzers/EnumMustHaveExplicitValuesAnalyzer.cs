using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC009: Public enum members must have explicit integer values to prevent
/// silent reordering breaks when members are added, removed, or rearranged.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EnumMustHaveExplicitValuesAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.EnumMustHaveExplicitValues,
        title: "Public enum members must have explicit values",
        messageFormat: "Enum member '{0}.{1}' does not have an explicit value. Add '= N' to prevent silent reordering breaks.",
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
        context.RegisterSyntaxNodeAction(AnalyzeEnumDeclaration, SyntaxKind.EnumDeclaration);
    }

    private static void AnalyzeEnumDeclaration(SyntaxNodeAnalysisContext context)
    {
        var enumDecl = (EnumDeclarationSyntax)context.Node;

        // Only check public enums
        var symbol = context.SemanticModel.GetDeclaredSymbol(enumDecl);
        if (symbol == null || symbol.DeclaredAccessibility != Accessibility.Public)
            return;

        foreach (var member in enumDecl.Members)
        {
            // Flag members without an explicit = N initializer
            if (member.EqualsValue == null)
            {
                var memberSymbol = context.SemanticModel.GetDeclaredSymbol(member);
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    member.GetLocation(),
                    symbol.Name,
                    memberSymbol?.Name ?? member.Identifier.Text));
            }
        }
    }
}
