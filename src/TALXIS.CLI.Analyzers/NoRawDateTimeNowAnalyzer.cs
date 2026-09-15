using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC019: Avoid <c>DateTime.Now</c> and <c>DateTimeOffset.Now</c> in command and
/// service classes. Raw wall-clock reads make code hard to test and lead to timestamp
/// inconsistencies across log surfaces. Prefer injecting a clock abstraction or, at
/// minimum, using <c>DateTimeOffset.UtcNow</c> for timestamps that will be serialised.
/// <para>
/// Infrastructure code (formatters, trace listeners) that intentionally uses local
/// time for human-readable output can suppress with <c>#pragma warning disable TXC019</c>.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoRawDateTimeNowAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoRawDateTimeNow,
        title: "Avoid DateTime.Now / DateTimeOffset.Now — prefer UTC or an injected clock",
        messageFormat: "'{0}' uses {1}. Consider DateTimeOffset.UtcNow or an injected clock for testability.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzePropertyReference, OperationKind.PropertyReference);
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context)
    {
        var propRef = (IPropertyReferenceOperation)context.Operation;
        var property = propRef.Property;

        if (property.Name != "Now")
            return;

        var typeName = property.ContainingType?.Name;
        if (typeName is not ("DateTime" or "DateTimeOffset"))
            return;

        var ns = property.ContainingType?.ContainingNamespace?.ToDisplayString();
        if (ns != "System")
            return;

        var containingTypeName = context.ContainingSymbol?.ContainingType?.Name
            ?? context.ContainingSymbol?.Name
            ?? "Unknown";

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            propRef.Syntax.GetLocation(),
            containingTypeName,
            $"{typeName}.Now"));
    }
}
