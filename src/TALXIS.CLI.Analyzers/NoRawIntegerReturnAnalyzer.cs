using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC005: In <c>ExecuteAsync()</c> methods on classes inheriting <c>TxcLeafCommand</c>,
/// flag <c>return &lt;integer-literal&gt;</c> statements. Use the named constants
/// <c>ExitSuccess</c>, <c>ExitError</c>, or <c>ExitValidationError</c> instead for
/// clarity and consistency.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoRawIntegerReturnAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoRawIntegerReturn,
        title: "Do not return raw integer literals from ExecuteAsync",
        messageFormat: "ExecuteAsync in '{0}' returns a raw integer literal '{1}'. Use ExitSuccess, ExitError, or ExitValidationError instead.",
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
        context.RegisterSyntaxNodeAction(AnalyzeReturnStatement, SyntaxKind.ReturnStatement);
    }

    private static void AnalyzeReturnStatement(SyntaxNodeAnalysisContext context)
    {
        var returnStatement = (ReturnStatementSyntax)context.Node;

        if (returnStatement.Expression == null)
            return;

        // Check if the return expression contains a numeric literal
        // Handles: return 0; return 1; return Task.FromResult(0); etc.
        var literal = FindNumericLiteral(returnStatement.Expression);
        if (literal == null)
            return;

        // Must be inside a method named ExecuteAsync
        var method = returnStatement.FirstAncestorOrSelf<MethodDeclarationSyntax>();
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
            literal.GetLocation(),
            typeSymbol.Name,
            literal.Token.Text));
    }

    /// <summary>
    /// Finds a numeric literal in the expression, including inside
    /// <c>Task.FromResult(N)</c> wrapper calls.
    /// </summary>
    private static LiteralExpressionSyntax? FindNumericLiteral(ExpressionSyntax expression)
    {
        if (expression is LiteralExpressionSyntax lit && lit.IsKind(SyntaxKind.NumericLiteralExpression))
            return lit;

        // Handle Task.FromResult(N)
        if (expression is InvocationExpressionSyntax invocation
            && invocation.ArgumentList.Arguments.Count == 1)
        {
            var arg = invocation.ArgumentList.Arguments[0].Expression;
            if (arg is LiteralExpressionSyntax argLit && argLit.IsKind(SyntaxKind.NumericLiteralExpression))
                return argLit;
        }

        return null;
    }
}
