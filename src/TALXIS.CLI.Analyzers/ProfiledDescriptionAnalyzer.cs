using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC011: Commands inheriting <c>ProfiledCliCommand</c> or <c>ProfiledLeafCliCommand</c>
/// should mention "profile" or "environment" in their <c>[CliCommand(Description)]</c>.
/// This ensures AI harnesses know a profile is required before attempting to call the tool.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ProfiledDescriptionAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] RequiredKeywords = { "profile", "environment", "live", "connected" };

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.ProfiledDescriptionContext,
        title: "Profiled command description should mention profile or environment",
        messageFormat: "'{0}' inherits ProfiledCliCommand but its description doesn't mention 'profile' or 'environment'. AI harnesses won't know a profile is required.",
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
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.IsAbstract || type.TypeKind != TypeKind.Class)
            return;

        // Check if it inherits from ProfiledCliCommand or ProfiledLeafCliCommand
        if (!RoslynHelpers.InheritsFrom(type, "TALXIS.CLI.Core.ProfiledCliCommand")
            && !RoslynHelpers.InheritsFrom(type, "TALXIS.CLI.Core.ProfiledLeafCliCommand"))
            return;

        var cliCommandAttr = GetAttribute(type, "CliCommandAttribute");
        if (cliCommandAttr is null)
            return;

        var description = GetNamedArgumentString(cliCommandAttr, "Description") ?? "";
        var lower = description.ToLowerInvariant();

        foreach (var keyword in RequiredKeywords)
        {
            if (lower.Contains(keyword))
                return; // At least one keyword found — no warning
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], type.Name));
    }

    private static AttributeData? GetAttribute(INamedTypeSymbol type, string attributeName)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.Name == attributeName)
                return attr;
        }
        return null;
    }

    private static string? GetNamedArgumentString(AttributeData attr, string name)
    {
        foreach (var arg in attr.NamedArguments)
        {
            if (arg.Key == name && arg.Value.Value is string s)
                return s;
        }
        return null;
    }
}
