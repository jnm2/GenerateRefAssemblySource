using Microsoft.CodeAnalysis;
using System.Collections.Generic;
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

        public static ImmutableArray<INamedTypeSymbol> RemoveBaseTypes(IEnumerable<INamedTypeSymbol> types)
        {
            var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();

            foreach (var incoming in types)
            {
                if (builder.Any(other => ImplementsOrInherits(other, incoming)))
                    continue;

                for (var i = builder.Count - 1; i >= 0; i--)
                {
                    if (ImplementsOrInherits(incoming, builder[i]))
                        builder.RemoveAt(i);
                }

                builder.Add(incoming);
            }

            return builder.ToImmutable();
        }

        public static bool ImplementsOrInherits(INamedTypeSymbol type, INamedTypeSymbol possibleBaseType)
        {
            foreach (var baseType in EnumerateBaseTypes(type))
            {
                if (SymbolEqualityComparer.Default.Equals(baseType, possibleBaseType))
                    return true;
            }

            foreach (var interfaceType in type.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(interfaceType, possibleBaseType))
                    return true;
            }

            return false;
        }

        public static IEnumerable<INamedTypeSymbol> EnumerateBaseTypes(INamedTypeSymbol type)
        {
            var current = type;
            while (true)
            {
                current = current.BaseType;
                if (current is null) break;
                yield return current;
            }
        }

        public static bool IsVisibleOutsideAssembly(ISymbol typeMember)
        {
            switch (typeMember.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    return typeMember.ContainingType is null || IsVisibleOutsideAssembly(typeMember.ContainingType);

                case Accessibility.Protected:
                case Accessibility.ProtectedOrInternal:
                    return IsVisibleOutsideAssembly(typeMember.ContainingType!) && IsInheritable(typeMember.ContainingType!);

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

        public static TypeMemberSortKind? GetTypeMemberSortKind(ISymbol symbol)
        {
            return symbol switch
            {
                IFieldSymbol { IsConst: true } => TypeMemberSortKind.Constant,
                IFieldSymbol => TypeMemberSortKind.Field,
                IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor } => TypeMemberSortKind.Constructor,
                IMethodSymbol { MethodKind: MethodKind.Destructor } => null, // Not public API
                IPropertySymbol { IsIndexer: true } => TypeMemberSortKind.Indexer,
                IPropertySymbol => TypeMemberSortKind.Property,
                IEventSymbol => TypeMemberSortKind.Event,
                IMethodSymbol { MethodKind: MethodKind.Ordinary } => TypeMemberSortKind.Method,
                IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } => TypeMemberSortKind.Operator,
                IMethodSymbol { MethodKind: MethodKind.Conversion } => TypeMemberSortKind.Conversion,
                IMethodSymbol { AssociatedSymbol: { } } => null, // Only the associated symbol is generated
                ITypeSymbol => null, // Nested types get their own files and type parameters are in the type header line
            };
        }
    }
}
