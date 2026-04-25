using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC003: Code inside <c>[CliCommand]</c> classes (and their nested helpers) must
/// not call <c>OutputWriter.Write</c> or <c>OutputWriter.WriteLine</c> directly.
/// Use <c>OutputFormatter</c> instead, which respects the <c>--format</c> flag.
/// <para>
/// Exception: text-renderer callbacks passed to <c>OutputFormatter.WriteList</c> etc.
/// legitimately call <c>OutputWriter</c> — those are allowed because they're only
/// invoked in text mode. This analyzer flags calls in the main execution path.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MustNotCallOutputWriterAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.MustNotCallOutputWriter,
        title: "Use OutputFormatter instead of OutputWriter in command code",
        messageFormat: "Direct call to OutputWriter.{0}() in command '{1}'. Use OutputFormatter instead to respect the --format flag.",
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

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.Syntax.GetLocation(),
            method.Name,
            containingType.Name));
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
