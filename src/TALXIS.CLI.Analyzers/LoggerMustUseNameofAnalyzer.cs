using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC016: CreateLogger calls must use <c>nameof()</c> for the category name.
/// <para>
/// The generic overload <c>CreateLogger&lt;T&gt;()</c> uses the full namespace-qualified
/// type name as the category, which produces noisy log output. The correct pattern is
/// <c>TxcLoggerFactory.CreateLogger(nameof(ClassName))</c>.
/// </para>
/// <para>
/// String-literal arguments are fragile — they silently go stale when a class is renamed.
/// Using <c>nameof()</c> keeps category names in sync with the code.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LoggerMustUseNameofAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor GenericRule = new(
        id: DiagnosticIds.LoggerMustUseNameof,
        title: "CreateLogger must use nameof() for the category name",
        messageFormat: "Use TxcLoggerFactory.CreateLogger(nameof({0})) instead of CreateLogger<{0}>() to ensure consistent short category names",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor LiteralRule = new(
        id: DiagnosticIds.LoggerMustUseNameof,
        title: "CreateLogger must use nameof() for the category name",
        messageFormat: "Use nameof() instead of a string literal in CreateLogger(\"{0}\") to prevent stale category names after refactoring",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(GenericRule, LiteralRule);

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

        if (method.Name != "CreateLogger")
            return;

        var containingTypeName = method.ContainingType?.Name;
        if (containingTypeName != "TxcLoggerFactory" && containingTypeName != "ILoggerFactory")
            return;

        // Do not flag calls inside TxcLoggerFactory itself — it is the factory implementation.
        if (context.ContainingSymbol?.ContainingType?.Name == "TxcLoggerFactory")
            return;

        // Generic overload: CreateLogger<T>() — always flag.
        if (method.IsGenericMethod)
        {
            var typeArg = method.TypeArguments[0];
            context.ReportDiagnostic(Diagnostic.Create(
                GenericRule,
                invocation.Syntax.GetLocation(),
                typeArg.Name));
            return;
        }

        // Non-generic overload: CreateLogger(string categoryName)
        if (invocation.Arguments.Length == 0)
            return;

        var firstArg = invocation.Arguments[0];

        // nameof(...) compiles to INameOfOperation — this is the correct pattern; skip it.
        if (firstArg.Value is INameOfOperation)
            return;

        // String literal — flag it.
        if (firstArg.Value is ILiteralOperation literal
            && literal.Type?.SpecialType == SpecialType.System_String)
        {
            var literalText = literal.ConstantValue.HasValue
                ? literal.ConstantValue.Value?.ToString() ?? ""
                : "";
            context.ReportDiagnostic(Diagnostic.Create(
                LiteralRule,
                firstArg.Syntax.GetLocation(),
                literalText));
            return;
        }

        // Interpolation, concatenation, or other dynamic expressions — intentional; skip.
    }
}
