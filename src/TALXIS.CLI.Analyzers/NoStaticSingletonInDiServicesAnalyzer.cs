using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace TALXIS.CLI.Analyzers;

/// <summary>
/// TXC024: Classes that receive dependencies via constructor injection should not
/// also access static singleton properties. Mixing DI with static singletons makes
/// the class harder to test (you need both constructor fakes AND static setup) and
/// hides real dependencies from the DI container.
/// <para>
/// This analyzer flags access to well-known static singletons
/// (<c>TxcTelemetrySetup.SessionResolver</c>, <c>TxcTelemetrySetup.TracerProvider</c>)
/// from within classes whose constructors accept at least one interface or abstract-type
/// parameter (a proxy for "this class participates in DI").
/// </para>
/// <para>
/// Bootstrap/setup classes and the singleton owners themselves are exempt.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NoStaticSingletonInDiServicesAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        id: DiagnosticIds.NoStaticSingletonInDiServices,
        title: "DI-injected classes should not access static singletons",
        messageFormat: "'{0}' receives dependencies via constructor but accesses static '{1}.{2}'. Inject it via the constructor instead.",
        category: "TALXIS.CLI.Design",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>
    /// Known static singleton properties that should be injected instead.
    /// Key: containing type name. Value: set of property names.
    /// </summary>
    private static readonly ImmutableDictionary<string, ImmutableHashSet<string>> KnownSingletons =
        ImmutableDictionary.CreateRange(new[]
        {
            new KeyValuePair<string, ImmutableHashSet<string>>(
                "TxcTelemetrySetup",
                ImmutableHashSet.Create("SessionResolver", "TracerProvider")),
        });

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

        // Is this a known static singleton access?
        if (!property.IsStatic)
            return;

        var ownerTypeName = property.ContainingType?.Name;
        if (ownerTypeName == null)
            return;

        if (!KnownSingletons.TryGetValue(ownerTypeName, out var flaggedProps))
            return;

        if (!flaggedProps.Contains(property.Name))
            return;

        // Determine the containing class
        var containingType = context.ContainingSymbol?.ContainingType;
        if (containingType == null)
            return;

        // Exempt the singleton owner itself and bootstrap/setup classes
        if (containingType.Name == ownerTypeName)
            return;
        if (containingType.Name.Contains("Bootstrap") || containingType.Name.Contains("Setup"))
            return;
        if (containingType.Name == "Program")
            return;

        // Only flag classes that participate in DI (have a constructor with interface/abstract params)
        if (!HasDiConstructor(containingType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            propRef.Syntax.GetLocation(),
            containingType.Name,
            ownerTypeName,
            property.Name));
    }

    /// <summary>
    /// Returns <c>true</c> if the type has at least one public constructor with an
    /// interface or abstract class parameter — a heuristic for DI participation.
    /// </summary>
    private static bool HasDiConstructor(INamedTypeSymbol type)
    {
        foreach (var ctor in type.Constructors)
        {
            if (ctor.DeclaredAccessibility != Accessibility.Public)
                continue;

            foreach (var param in ctor.Parameters)
            {
                if (param.Type.TypeKind == TypeKind.Interface)
                    return true;
                if (param.Type.IsAbstract && param.Type.TypeKind == TypeKind.Class)
                    return true;
            }
        }

        return false;
    }
}
