using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC020: String literals containing the TALXIS repository URL should use
/// <c>TxcConstants.RepositoryIssuesUrl</c> (or other constants) instead of
/// duplicating the URL across multiple files. This prevents drift when the
/// repository is renamed or the URL changes.
/// <para>
/// The <c>TxcConstants</c> class itself and analyzer <c>helpLinkUri</c> attributes
/// are exempt.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoHardcodedRepoUrlAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// The URL prefix that should be centralised in TxcConstants.
    /// </summary>
    private const string RepoUrlPrefix = "https://github.com/TALXIS/tools-cli";

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoHardcodedRepoUrl,
        title: "Use TxcConstants instead of hardcoded repository URLs",
        messageFormat: "'{0}' contains a hardcoded repository URL. Use TxcConstants.RepositoryIssuesUrl or add a new constant in TxcConstants.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeLiteral, OperationKind.Literal);
    }

    private static void AnalyzeLiteral(OperationAnalysisContext context)
    {
        var literal = (ILiteralOperation)context.Operation;

        if (literal.Type?.SpecialType != SpecialType.System_String)
            return;

        if (!literal.ConstantValue.HasValue)
            return;

        var value = literal.ConstantValue.Value as string;
        if (value == null || !value.Contains(RepoUrlPrefix))
            return;

        // Allow inside TxcConstants itself — that's where the constant lives.
        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType?.Name == "TxcConstants")
            return;

        // Allow in DiagnosticDescriptor helpLinkUri parameters (analyzers reference docs).
        // These are compile-time const strings in analyzer rule definitions.
        if (containingType?.ContainingNamespace?.ToDisplayString()?.StartsWith("TALXIS.CLI.Analyzers") == true)
            return;

        var containingTypeName = containingType?.Name
            ?? context.ContainingSymbol?.Name
            ?? "Unknown";

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            literal.Syntax.GetLocation(),
            containingTypeName));
    }
}
