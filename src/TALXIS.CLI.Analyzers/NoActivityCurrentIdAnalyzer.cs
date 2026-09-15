using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC021: Do not access <c>Activity.Current?.Id</c> (or <c>.TraceId</c>,
/// <c>.SpanId</c>) directly. Use <c>TxcActivitySource.CurrentOperationId</c>
/// which provides a consistent, null-safe operation identifier with a
/// well-known fallback value.
/// <para>
/// The <c>TxcActivitySource</c> class itself is exempt.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoActivityCurrentIdAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoActivityCurrentId,
        title: "Use TxcActivitySource.CurrentOperationId instead of Activity.Current?.Id",
        messageFormat: "'{0}' accesses Activity.Current.{1}. Use TxcActivitySource.CurrentOperationId for a consistent operation identifier.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Property names on <c>Activity</c> that represent identity and should go
    /// through the centralised accessor.
    /// </summary>
    private static readonly ImmutableHashSet<string> FlaggedProperties = ImmutableHashSet.Create(
        "Id",
        "TraceId",
        "SpanId");

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

        if (!FlaggedProperties.Contains(property.Name))
            return;

        // The property must be on System.Diagnostics.Activity
        if (property.ContainingType?.Name != "Activity"
            || property.ContainingType.ContainingNamespace?.ToDisplayString() != "System.Diagnostics")
            return;

        // Check that the receiver is Activity.Current (static property access)
        if (propRef.Instance is not IPropertyReferenceOperation parentPropRef)
            return;

        if (parentPropRef.Property.Name != "Current")
            return;

        // Allow inside TxcActivitySource — it implements the accessor
        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType?.Name == "TxcActivitySource")
            return;

        var containingTypeName = containingType?.Name
            ?? context.ContainingSymbol?.Name
            ?? "Unknown";

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            propRef.Syntax.GetLocation(),
            containingTypeName,
            property.Name));
    }
}
