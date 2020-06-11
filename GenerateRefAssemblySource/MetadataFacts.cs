using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Linq;

namespace GenerateRefAssemblySource
{
    internal static class MetadataFacts
    {
        public static ImmutableArray<INamedTypeSymbol> GetContainingTypes(INamedTypeSymbol type)
        {
            var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
            var current = type;

            while (true)
            {
                current = current.ContainingType;
                if (current is null) break;
                builder.Add(current);
            }

            builder.Reverse();
            return builder.ToImmutable();
        }

        public static bool IsVisibleOutsideAssembly(INamedTypeSymbol type)
        {
            switch (type.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    return type.ContainingType is null || IsVisibleOutsideAssembly(type.ContainingType);

                case Accessibility.Protected:
                case Accessibility.ProtectedOrInternal:
                    return IsVisibleOutsideAssembly(type.ContainingType!) && IsInheritable(type.ContainingType!);

                default:
                    return false;
            }
        }

        public static bool IsInheritable(INamedTypeSymbol type)
        {
            return !type.IsSealed && type.GetMembers(WellKnownMemberNames.InstanceConstructorName)
                .OfType<IMethodSymbol>()
                .Any(IsVisibleToDerivedTypes);
        }

        public static bool IsVisibleToDerivedTypes(IMethodSymbol method)
        {
            switch (method.DeclaredAccessibility)
            {
                case Accessibility.Public:
                case Accessibility.Protected:
                case Accessibility.ProtectedOrInternal:
                    return true;

                default:
                    return false;
            }
        }
    }
}
