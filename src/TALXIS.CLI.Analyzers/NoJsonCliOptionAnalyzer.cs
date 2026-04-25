using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC007: Leaf commands inheriting <c>TxcLeafCommand</c> must not declare a
/// <c>[CliOption(Name = "--json")]</c> property. The base class provides a
/// <c>--format</c> flag that supports JSON output; use that instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoJsonCliOptionAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoJsonCliOption,
        title: "Do not declare a --json CLI option",
        messageFormat: "'{0}' declares a --json CLI option. Use the inherited --format flag (json/text) from TxcLeafCommand instead.",
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
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;

        // Check for [CliOption] attribute with Name = "--json"
        foreach (var attr in property.GetAttributes())
        {
            if (attr.AttributeClass?.Name != "CliOptionAttribute")
                continue;

            // Check named arguments for Name = "--json"
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "Name"
                    && namedArg.Value.Value is string name
                    && name.Equals("--json", System.StringComparison.OrdinalIgnoreCase))
                {
                    // Verify the containing type inherits TxcLeafCommand
                    var containingType = property.ContainingType;
                    if (containingType != null && RoslynHelpers.InheritsFrom(containingType, "TALXIS.CLI.Core.TxcLeafCommand"))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            Rule,
                            property.Locations[0],
                            containingType.Name));
                    }
                    return;
                }
            }

            // Also check constructor arguments (first positional argument can be the name)
            if (attr.ConstructorArguments.Length > 0
                && attr.ConstructorArguments[0].Value is string ctorName
                && ctorName.Equals("--json", System.StringComparison.OrdinalIgnoreCase))
            {
                var containingType = property.ContainingType;
                if (containingType != null && RoslynHelpers.InheritsFrom(containingType, "TALXIS.CLI.Core.TxcLeafCommand"))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rule,
                        property.Locations[0],
                        containingType.Name));
                }
                return;
            }

            // Check Aliases for "--json"
            foreach (var namedArg in attr.NamedArguments)
            {
                if (namedArg.Key == "Aliases" && !namedArg.Value.Values.IsDefaultOrEmpty)
                {
                    foreach (var alias in namedArg.Value.Values)
                    {
                        if (alias.Value is string aliasStr
                            && aliasStr.Equals("--json", System.StringComparison.OrdinalIgnoreCase))
                        {
                            var containingType = property.ContainingType;
                            if (containingType != null && RoslynHelpers.InheritsFrom(containingType, "TALXIS.CLI.Core.TxcLeafCommand"))
                            {
                                context.ReportDiagnostic(Diagnostic.Create(
                                    Rule,
                                    property.Locations[0],
                                    containingType.Name));
                            }
                            return;
                        }
                    }
                }
            }
        }
    }
}
