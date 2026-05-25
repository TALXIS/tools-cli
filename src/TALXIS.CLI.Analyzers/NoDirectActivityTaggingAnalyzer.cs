using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC025: Command code must not call <c>Activity.SetTag()</c>, <c>Activity.SetStatus()</c>,
/// <c>Activity.AddEvent()</c>, or <c>Activity.RecordException()</c> directly.
/// Commands should use <c>CommandActivityScope</c> (managed by <c>TxcLeafCommand.RunAsync</c>)
/// and <c>ILogger</c> (bridged by <c>TxcTelemetryLogProvider</c>) for telemetry.
/// Direct Activity manipulation bypasses the standardized pipeline and can create
/// inconsistent tag names or conflicting status overwrites.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoDirectActivityTaggingAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoDirectActivityTagging,
        title: "Do not call Activity.SetTag/SetStatus/AddEvent/RecordException in command code",
        messageFormat: "'{0}' directly calls Activity.{1}(). Use CommandActivityScope and ILogger instead.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly string[] BannedMethods =
        ["SetTag", "SetStatus", "AddEvent", "RecordException"];

    /// <summary>
    /// Telemetry infrastructure types that legitimately manipulate Activity.
    /// </summary>
    private static readonly string[] ExemptTypes =
    [
        "CommandActivityScope",
        "TxcTelemetryLogProvider",
        "TxcTelemetryLogger",
        "SessionIdActivityProcessor",
        "ActivityIdentityTagger",
        "TxcActivitySource"
    ];

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

        // Check if this is a call to Activity.SetTag/SetStatus/AddEvent/RecordException
        if (Array.IndexOf(BannedMethods, method.Name) < 0)
            return;

        var receiverType = method.ContainingType;
        if (receiverType?.Name != "Activity" || receiverType.ContainingNamespace?.ToDisplayString() != "System.Diagnostics")
            return;

        // Check if we're inside a [CliCommand] class
        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType == null)
            return;

        // Exempt telemetry infrastructure types
        if (Array.IndexOf(ExemptTypes, containingType.Name) >= 0)
            return;

        // Only flag [CliCommand]-attributed types and their ancestors
        if (!HasCliCommandAttribute(containingType) && !InheritsFromTxcLeafCommand(containingType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.Syntax.GetLocation(),
            containingType.Name,
            method.Name));
    }

    private static bool HasCliCommandAttribute(INamedTypeSymbol type)
    {
        return type.GetAttributes().Any(a =>
            a.AttributeClass?.Name is "CliCommandAttribute");
    }

    private static bool InheritsFromTxcLeafCommand(INamedTypeSymbol type)
    {
        return RoslynHelpers.InheritsFrom(type, "TALXIS.CLI.Core.Shared.TxcLeafCommand");
    }
}
