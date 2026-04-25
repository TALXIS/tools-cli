using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC006: In <c>ExecuteAsync()</c> methods on classes inheriting <c>TxcLeafCommand</c>,
/// flag <c>try { } catch { }</c> blocks. The base class <c>RunAsync</c> already provides
/// standardized error handling. <c>try { } finally { }</c> is allowed (for cleanup).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoTryCatchInExecuteAsyncAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoTryCatchInExecuteAsync,
        title: "Do not use try-catch in ExecuteAsync",
        messageFormat: "ExecuteAsync in '{0}' contains a try-catch block. Remove it — the base class RunAsync() provides standardized error handling. Use try-finally for cleanup if needed.",
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
        context.RegisterSyntaxNodeAction(AnalyzeTryStatement, SyntaxKind.TryStatement);
    }

    private static void AnalyzeTryStatement(SyntaxNodeAnalysisContext context)
    {
        var tryStatement = (TryStatementSyntax)context.Node;

        // Allow try-finally without catch clauses
        if (tryStatement.Catches.Count == 0)
            return;

        // Must be inside a method named ExecuteAsync
        var method = tryStatement.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method == null || method.Identifier.Text != "ExecuteAsync")
            return;

        // The containing class must inherit TxcLeafCommand
        var classDecl = method.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (classDecl == null)
            return;

        var typeSymbol = context.SemanticModel.GetDeclaredSymbol(classDecl);
        if (typeSymbol == null || !RoslynHelpers.InheritsFrom(typeSymbol, "TALXIS.CLI.Core.TxcLeafCommand"))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            tryStatement.TryKeyword.GetLocation(),
            typeSymbol.Name));
    }
}
