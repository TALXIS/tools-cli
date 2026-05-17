using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC015: Catch blocks must not silently swallow exceptions. Broad catch clauses
/// (catching <c>Exception</c>, <c>SystemException</c>, or bare <c>catch</c>) must
/// contain at least one of: a call to a Log* method, a throw/rethrow, or a return statement.
/// Narrowly-typed catches (e.g. <c>OperationCanceledException</c>, <c>IOException</c>) are
/// not flagged — they represent intentional, targeted error handling.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoBareExceptionSwallowAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoBareExceptionSwallow,
        title: "Catch blocks must not silently swallow exceptions",
        messageFormat: "Catch block in '{0}' silently swallows {1}. Add logging (Logger.LogError), rethrow, or return an error result.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Broad exception types that are flagged when caught without logging, rethrowing, or returning.
    /// </summary>
    private static readonly ImmutableHashSet<string> BroadExceptionTypeNames = ImmutableHashSet.Create(
        "Exception",
        "SystemException");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeCatchClause, OperationKind.CatchClause);
    }

    private static void AnalyzeCatchClause(OperationAnalysisContext context)
    {
        var catchOp = (ICatchClauseOperation)context.Operation;

        // Determine whether this is a broad catch.
        // A bare "catch { }" has ExceptionType == null → broad.
        // Otherwise check against the known broad type names.
        var exceptionType = catchOp.ExceptionType;
        if (exceptionType != null && !IsBroadExceptionType(exceptionType))
            return;

        // Walk the catch handler body looking for an acceptable action.
        var handler = catchOp.Handler;
        if (handler != null && ContainsAcceptableAction(handler))
            return;

        // Build diagnostic arguments.
        var caughtDescription = exceptionType != null
            ? exceptionType.Name
            : "all exceptions";

        var containingTypeName = context.ContainingSymbol is IMethodSymbol ms
            ? ms.ContainingType?.Name ?? ms.Name
            : context.ContainingSymbol?.ContainingType?.Name ?? context.ContainingSymbol?.Name ?? "Unknown";

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            catchOp.Syntax.GetLocation(),
            containingTypeName,
            caughtDescription));
    }

    /// <summary>
    /// Returns <c>true</c> when the caught type is <c>System.Exception</c> or <c>System.SystemException</c>.
    /// We check the simple name AND verify the type lives in the <c>System</c> namespace to avoid
    /// false positives on user-defined types that happen to share the name.
    /// </summary>
    private static bool IsBroadExceptionType(ITypeSymbol type)
    {
        if (!BroadExceptionTypeNames.Contains(type.Name))
            return false;

        return type.ContainingNamespace?.ToDisplayString() == "System";
    }

    /// <summary>
    /// Walks all descendant operations of <paramref name="operation"/> looking for at least one
    /// acceptable action: a Log* method call, a throw/rethrow, or a return statement.
    /// </summary>
    private static bool ContainsAcceptableAction(IOperation operation)
    {
        foreach (var descendant in operation.Descendants())
        {
            switch (descendant.Kind)
            {
                // throw or rethrow (bare "throw;" is also OperationKind.Throw with null exception)
                case OperationKind.Throw:
                    return true;

                // return statement
                case OperationKind.Return:
                    return true;

                // method call whose name starts with "Log"
                case OperationKind.Invocation:
                    var invocation = (IInvocationOperation)descendant;
                    if (invocation.TargetMethod.Name.StartsWith("Log"))
                        return true;
                    break;
            }
        }

        return false;
    }
}
