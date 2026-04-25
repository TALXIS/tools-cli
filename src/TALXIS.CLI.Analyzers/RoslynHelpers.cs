using Microsoft.CodeAnalysis;

namespace TALXIS.CLI.Analyzers;

internal static class RoslynHelpers
{
    internal static bool InheritsFrom(INamedTypeSymbol type, string fullName)
    {
        var current = type.BaseType;
        while (current != null)
        {
            if (current.ToDisplayString() == fullName)
                return true;
            current = current.BaseType;
        }
        return false;
    }
}
