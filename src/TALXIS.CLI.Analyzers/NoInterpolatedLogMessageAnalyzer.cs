using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC014: Logger calls must use message templates with named placeholders instead of
/// string interpolation. Interpolated strings lose structured data — the key-value pairs
/// that feed the <c>data</c> dict in the output contract are discarded.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoInterpolatedLogMessageAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// ILogger extension method names that accept a message template parameter.
    /// </summary>
    private static readonly HashSet<string> LogMethodNames = new()
    {
        "Log",
        "LogTrace",
        "LogDebug",
        "LogInformation",
        "LogWarning",
        "LogError",
        "LogCritical"
    };

    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoInterpolatedLogMessage,
        title: "Use message templates instead of string interpolation in logger calls",
        messageFormat: "Logger call in '{0}' uses string interpolation. Use a message template with named placeholders (e.g., Logger.LogError(\"Failed for {{Entity}}\", entity)) to preserve structured log data.",
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

        if (!LogMethodNames.Contains(method.Name))
            return;

        if (!IsLoggerMethod(method))
            return;

        // Find the message/format string argument.
        // For the Log() overload the message parameter is named "message" or is the format string.
        // For LogXxx() extension methods the first string parameter is the message template.
        var messageArgument = FindMessageArgument(invocation);
        if (messageArgument == null)
            return;

        // Check if the argument value is an interpolated string
        if (messageArgument.Value is not IInterpolatedStringOperation)
            return;

        // Determine containing type name for the diagnostic message
        var containingSymbol = context.ContainingSymbol;
        var containingTypeName = containingSymbol is IMethodSymbol ms
            ? ms.ContainingType?.Name ?? ms.Name
            : containingSymbol?.ContainingType?.Name ?? containingSymbol?.Name ?? "Unknown";

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            messageArgument.Syntax.GetLocation(),
            containingTypeName));
    }

    /// <summary>
    /// Checks whether the method is an ILogger instance method or a LoggerExtensions static method.
    /// </summary>
    private static bool IsLoggerMethod(IMethodSymbol method)
    {
        var containingType = method.ContainingType;
        if (containingType == null)
            return false;

        // Direct call on a type implementing ILogger
        if (ImplementsILogger(containingType))
            return true;

        // Static extension methods in LoggerExtensions
        if (containingType.Name == "LoggerExtensions"
            && containingType.ToDisplayString().StartsWith("Microsoft.Extensions.Logging.LoggerExtensions"))
            return true;

        // Check if this is an extension method whose receiver type is ILogger
        if (method.IsExtensionMethod && method.ReducedFrom != null)
        {
            var receiverType = method.ReducedFrom.Parameters.FirstOrDefault()?.Type;
            if (receiverType != null && IsILoggerType(receiverType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the type is or implements <c>Microsoft.Extensions.Logging.ILogger</c>.
    /// </summary>
    private static bool ImplementsILogger(INamedTypeSymbol type)
    {
        if (IsILoggerType(type))
            return true;

        return type.AllInterfaces.Any(i => IsILoggerType(i));
    }

    private static bool IsILoggerType(ITypeSymbol type)
    {
        return type.Name == "ILogger"
               && type.ContainingNamespace?.ToDisplayString() == "Microsoft.Extensions.Logging";
    }

    /// <summary>
    /// Finds the message template argument in a logger invocation.
    /// Looks for parameters named "message" or "format", or falls back to the first string parameter.
    /// </summary>
    private static IArgumentOperation? FindMessageArgument(IInvocationOperation invocation)
    {
        // Try to find by well-known parameter names first
        foreach (var arg in invocation.Arguments)
        {
            var paramName = arg.Parameter?.Name;
            if (paramName == "message" || paramName == "format" || paramName == "formatString")
                return arg;
        }

        // Fall back to first string-typed parameter
        foreach (var arg in invocation.Arguments)
        {
            if (arg.Parameter?.Type?.SpecialType == SpecialType.System_String)
                return arg;
        }

        return null;
    }
}
