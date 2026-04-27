using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC012: Commands marked <c>[CliDestructive]</c> should signal danger in their
/// <c>[CliCommand(Description)]</c> by including words like "delete", "remove",
/// "uninstall", "destroy", or "permanently". This ensures both humans reading
/// CLI help and AI harnesses understand the risk before invocation.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DestructiveDescriptionAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] DangerKeywords =
    {
        "delete", "remove", "uninstall", "destroy", "permanently", "destructive",
        "discard", "drop", "purge", "erase", "wipe"
    };

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.DestructiveDescriptionSignal,
        title: "[CliDestructive] command description should signal danger",
        messageFormat: "'{0}' is [CliDestructive] but its description doesn't contain danger words (delete, remove, uninstall, etc.). The description should clearly signal that this operation is destructive.",
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

        if (!HasAttribute(type, "CliDestructiveAttribute"))
            return;

        var cliCommandAttr = GetAttribute(type, "CliCommandAttribute");
        if (cliCommandAttr is null)
            return;

        var description = GetNamedArgumentString(cliCommandAttr, "Description") ?? "";
        var lower = description.ToLowerInvariant();

        foreach (var keyword in DangerKeywords)
        {
            if (lower.Contains(keyword))
                return; // Danger word found
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], type.Name));
    }

    private static bool HasAttribute(INamedTypeSymbol type, string attributeName)
    {
        foreach (var attr in type.GetAttributes())
        {
            if (attr.AttributeClass?.Name == attributeName)
                return true;
        }
        return false;
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
