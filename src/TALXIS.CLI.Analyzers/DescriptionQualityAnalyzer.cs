using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC010: Every non-abstract leaf <c>[CliCommand]</c> class must have a <c>Description</c>
/// of at least 20 characters. Short or empty descriptions degrade AI tool discovery quality
/// in the MCP progressive disclosure system and produce unhelpful CLI help text.
/// <para>
/// Routing/hub commands (void Run(CliContext)) and <c>[McpIgnore]</c> commands are exempt.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DescriptionQualityAnalyzer : DiagnosticAnalyzer
{
    private const int MinDescriptionLength = 20;

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.DescriptionMinLength,
        title: "CLI command description is too short",
        messageFormat: "'{0}' has a description of only {1} characters (minimum {2}). Write a meaningful description that explains WHAT the command does and WHEN to use it.",
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

        var cliCommandAttr = GetAttribute(type, "CliCommandAttribute");
        if (cliCommandAttr is null)
            return;

        // Routing/hub commands are exempt
        if (IsRoutingCommand(type))
            return;

        // [McpIgnore] commands are exempt
        if (HasAttribute(type, "McpIgnoreAttribute"))
            return;

        // Extract Description from the attribute
        var description = GetNamedArgumentString(cliCommandAttr, "Description") ?? "";

        if (description.Length < MinDescriptionLength)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule, type.Locations[0], type.Name, description.Length, MinDescriptionLength));
        }
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

    private static bool HasAttribute(INamedTypeSymbol type, string attributeName)
    {
        return GetAttribute(type, attributeName) is not null;
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

    private static bool IsRoutingCommand(INamedTypeSymbol type)
    {
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
