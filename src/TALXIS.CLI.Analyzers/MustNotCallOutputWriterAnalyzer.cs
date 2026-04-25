using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC003: Code inside <c>[CliCommand]</c> classes (and their nested helpers) must
/// not call <c>OutputWriter.Write</c> or <c>OutputWriter.WriteLine</c> directly.
/// Use <c>OutputFormatter</c> instead, which respects the <c>--format</c> flag.
/// <para>
/// Exception: <c>OutputWriter</c> calls inside <b>lambda/anonymous method</b> text-renderer
/// callbacks passed to <c>OutputFormatter.WriteList</c>, <c>WriteData</c>, <c>WriteRaw</c>,
/// or <c>WriteDynamicTable</c> are automatically suppressed — they're only invoked in text mode.
/// </para>
/// <para>
/// <b>Named methods</b> passed as method-group text renderers (e.g., <c>OutputFormatter.WriteList(items, PrintTable)</c>)
/// are NOT auto-suppressed. Use <c>#pragma warning disable TXC003</c> on those methods with a comment.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotCallOutputWriterAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// OutputFormatter methods that accept text-renderer callbacks.
    /// OutputWriter calls inside lambdas passed to these methods are legitimate.
    /// </summary>
    private static readonly HashSet<string> OutputFormatterRendererMethods = new()
    {
        "WriteData",
        "WriteList",
        "WriteRaw",
        "WriteDynamicTable"
    };

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.MustNotCallOutputWriter,
        title: "Use OutputFormatter instead of OutputWriter in command code",
        messageFormat: "Direct call to OutputWriter.{0}() in command '{1}'. Use OutputFormatter instead to respect the --format flag.",
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
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;

        // Check if calling OutputWriter.Write or OutputWriter.WriteLine
        if (method.ContainingType?.Name != "OutputWriter"
            || method.ContainingType.ToDisplayString() != "TALXIS.CLI.Core.OutputWriter")
            return;

        if (method.Name != "Write" && method.Name != "WriteLine")
            return;

        // Find the containing type — only flag if it's a [CliCommand] class
        var containingType = GetContainingCliCommandType(context.ContainingSymbol);
        if (containingType == null)
            return;

        // Skip if the containing type IS OutputFormatter (infrastructure, not command code)
        if (containingType.Name == "OutputFormatter")
            return;

        // Suppress if the call is inside a lambda/anonymous method passed as
        // an argument to an OutputFormatter renderer method (WriteData, WriteList, etc.)
        if (IsInsideOutputFormatterRendererLambda(invocation, context.Compilation))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.Syntax.GetLocation(),
            method.Name,
            containingType.Name));
    }

    /// <summary>
    /// Walks up the syntax tree from the OutputWriter invocation to find an enclosing
    /// lambda/anonymous method. If found, checks whether that lambda is an argument
    /// to an <c>OutputFormatter</c> renderer method (WriteData, WriteList, etc.).
    /// </summary>
    private static bool IsInsideOutputFormatterRendererLambda(
        IInvocationOperation outputWriterCall,
        Compilation compilation)
    {
        var syntaxNode = outputWriterCall.Syntax;

        // Walk up syntax ancestors looking for a lambda or anonymous method
        foreach (var ancestor in syntaxNode.Ancestors())
        {
            if (ancestor is not (LambdaExpressionSyntax or AnonymousMethodExpressionSyntax))
                continue;

            // Found a lambda — check if it's an argument to an OutputFormatter method.
            // Expected tree: Lambda → Argument → ArgumentList → InvocationExpression
            if (ancestor.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: InvocationExpressionSyntax parentInvocation } })
            {
                if (IsOutputFormatterRendererCall(parentInvocation, compilation))
                    return true;
            }

            // Stop at the first enclosing lambda — don't look past it, because
            // a lambda inside a lambda should only be judged by its immediate parent.
            break;
        }

        return false;
    }

    /// <summary>
    /// Checks whether an <see cref="InvocationExpressionSyntax"/> is a call to one
    /// of the known OutputFormatter renderer methods.
    /// </summary>
    private static bool IsOutputFormatterRendererCall(
        InvocationExpressionSyntax invocation,
        Compilation compilation)
    {
        // Resolve the method name from the invocation expression syntax
        string? methodName = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            _ => null
        };

        if (methodName == null || !OutputFormatterRendererMethods.Contains(methodName))
            return false;

        // Verify the containing type is actually OutputFormatter via the semantic model
        var semanticModel = compilation.GetSemanticModel(invocation.SyntaxTree);
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        var targetMethod = symbolInfo.Symbol as IMethodSymbol ?? symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        return targetMethod?.ContainingType?.ToDisplayString() == "TALXIS.CLI.Core.OutputFormatter";
    }

    private static INamedTypeSymbol? GetContainingCliCommandType(ISymbol? symbol)
    {
        while (symbol != null)
        {
            if (symbol is INamedTypeSymbol type)
            {
                foreach (var attr in type.GetAttributes())
                {
                    if (attr.AttributeClass?.Name == "CliCommandAttribute")
                        return type;
                }
            }
            symbol = symbol.ContainingSymbol;
        }
        return null;
    }
}
