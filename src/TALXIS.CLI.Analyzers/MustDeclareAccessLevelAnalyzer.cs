using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC004: Every non-abstract leaf <c>[CliCommand]</c> class must carry at least one of
/// <c>[CliDestructive("…")]</c>, <c>[CliReadOnly]</c>, or <c>[CliIdempotent]</c>.
/// This forces developers to consciously declare the safety posture of every
/// command at design time.
/// <para>
/// <c>[CliDestructive]</c> and <c>[CliReadOnly]</c> remain mutually exclusive.
/// <c>[CliIdempotent]</c> satisfies the rule on its own and may also be combined
/// with either of the other two.
/// </para>
/// <para>
/// Commands with <c>[McpIgnore]</c> are exempt (they are excluded from both MCP and
/// the CLI confirmation pipeline).
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustDeclareAccessLevelAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor MissingRule = new(
        id: DiagnosticIds.MustDeclareAccessLevel,
        title: "Leaf CLI command must declare [CliDestructive], [CliReadOnly], or [CliIdempotent]",
        messageFormat: "'{0}' has [CliCommand] but lacks a safety annotation. Add [CliDestructive], [CliReadOnly], or [CliIdempotent].",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/TALXIS/tools-cli/blob/main/docs/output-contract.md");

    private static readonly DiagnosticDescriptor BothRule = new(
        id: DiagnosticIds.MustDeclareAccessLevel,
        title: "Leaf CLI command must not have both [CliDestructive] and [CliReadOnly]",
        messageFormat: "'{0}' has both [CliDestructive] and [CliReadOnly]. A command cannot be both destructive and read-only — pick one.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/TALXIS/tools-cli/blob/main/docs/output-contract.md");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(MissingRule, BothRule);

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

        if (!HasAttribute(type, "CliCommandAttribute"))
            return;

        // Routing/hub commands (void Run(CliContext)) are not leaf commands.
        if (IsRoutingCommand(type))
            return;

        // Commands marked [McpIgnore] are exempt — they're excluded from both pipelines.
        if (HasAttribute(type, "McpIgnoreAttribute"))
            return;

        bool hasDestructive = HasAttribute(type, "CliDestructiveAttribute");
        bool hasReadOnly = HasAttribute(type, "CliReadOnlyAttribute");
        bool hasIdempotent = HasAttribute(type, "CliIdempotentAttribute");

        if (hasDestructive && hasReadOnly)
        {
            context.ReportDiagnostic(Diagnostic.Create(BothRule, type.Locations[0], type.Name));
        }
        else if (!hasDestructive && !hasReadOnly && !hasIdempotent)
        {
            context.ReportDiagnostic(Diagnostic.Create(MissingRule, type.Locations[0], type.Name));
        }
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
