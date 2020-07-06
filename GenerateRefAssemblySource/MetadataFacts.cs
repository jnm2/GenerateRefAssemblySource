using Microsoft.CodeAnalysis;
using System;
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

        public static FlagsEnumSolver.Operation? GetCombinedEnumMembers(ITypeSymbol enumType, object? value)
        {
            if (enumType.TypeKind != TypeKind.Enum)
                throw new ArgumentException("An enum type must be specified.", nameof(enumType));

            var firstMemberWithSameValue = enumType.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => f.HasConstantValue && Equals(f.ConstantValue, value))
                .FirstOrDefault(); // Don't sort alphabetically; it won't keep things stable when new items are added anyway.

            return
                firstMemberWithSameValue is not null ? new FlagsEnumSolver.EnumMemberOperation(firstMemberWithSameValue) :
                IsFlagsEnum(enumType) ? new FlagsEnumSolver(enumType).Solve(Convert.ToUInt64(value)) :
                null;
        }

        public static bool IsFlagsEnum(ITypeSymbol enumType)
        {
            if (enumType.TypeKind != TypeKind.Enum)
                throw new ArgumentException("An enum type must be specified.", nameof(enumType));

            foreach (var attribute in enumType.GetAttributes())
            {
                if (attribute.AttributeConstructor is { Parameters: { IsEmpty: true } }
                    && IsFlagsAttribute(attribute.AttributeConstructor.ContainingType))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsFlagsAttribute(ITypeSymbol type)
        {
            return type is
            {
                Name: nameof(FlagsAttribute),
                ContainingSymbol: INamespaceSymbol
                {
                    Name: nameof(System),
                    ContainingSymbol: INamespaceSymbol
                    {
                        IsGlobalNamespace: true
                    }
                }
            };
        }

        public static bool IsPrimitiveType(ITypeSymbol type)
        {
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                case SpecialType.System_Byte:
                case SpecialType.System_Char:
                case SpecialType.System_Decimal:
                case SpecialType.System_Double:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                case SpecialType.System_Int64:
                case SpecialType.System_Object:
                case SpecialType.System_SByte:
                case SpecialType.System_Single:
                case SpecialType.System_String:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                case SpecialType.System_UInt64:
                case SpecialType.System_Void:
                    return true;

                default:
                    return false;
            }
        }

        public static ISymbol? GetOverriddenMember(ISymbol overridingMember)
        {
            return overridingMember switch
            {
                IMethodSymbol m => m.OverriddenMethod,
                IPropertySymbol p => p.OverriddenProperty,
                IEventSymbol e => e.OverriddenEvent,
                { IsOverride: true } => throw new NotImplementedException("Member is an override but is not a method, property, or event"),
                _ => null,
            };
        }

        public static bool HidesBaseMember(ISymbol member)
        {
            if (member.IsImplicitlyDeclared || member.IsOverride) return false;
            if (member is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor or MethodKind.Destructor }) return false;

            var baseTypes = member.ContainingType.TypeKind == TypeKind.Interface
                ? member.ContainingType.AllInterfaces
                : EnumerateBaseTypes(member.ContainingType);

            foreach (var baseType in baseTypes)
            {
                if (member is IPropertySymbol { IsIndexer: true } property)
                {
                    foreach (var baseProperty in baseType.GetMembers().OfType<IPropertySymbol>())
                    {
                        if (!IsVisibleOutsideAssembly(baseProperty)) continue;

                        if (AreSignaturesEqual(property.Parameters, baseProperty.Parameters)) return true;
                    }
                }
                else
                {
                    foreach (var baseMember in baseType.GetMembers(member.Name))
                    {
                        if (!IsVisibleOutsideAssembly(baseMember)) continue;

                        if (member is IMethodSymbol method && baseMember is IMethodSymbol baseMethod)
                        {
                            if (method.Arity == baseMethod.Arity
                                && AreSignaturesEqual(method.Parameters, baseMethod.Parameters))
                            {
                                return true;
                            }
                        }
                        else
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public static bool AreSignaturesEqual(ImmutableArray<IParameterSymbol> left, ImmutableArray<IParameterSymbol> right)
        {
            if (left.Length != right.Length) return false;

            foreach (var (leftParam, rightParam) in left.Zip(right))
            {
                if ((leftParam.RefKind == RefKind.None) != (rightParam.RefKind == RefKind.None)) return false;
                if (!SymbolEqualityComparer.Default.Equals(leftParam.Type, rightParam.Type)) return false;
            }

            return true;
        }

        public static bool HasFullName(this INamespaceOrTypeSymbol symbol, params string[] segments)
        {
            if (segments.Length < 1)
                throw new ArgumentException("At least one name segment must be specified.", nameof(segments));

            if (segments.Any(string.IsNullOrEmpty))
                throw new ArgumentException("Name segments must not be null or empty.", nameof(segments));

            if (symbol.ContainingType is not null) return false;
            if (symbol.Name != segments.Last()) return false;

            var currentNamespace = symbol.ContainingNamespace;

            for (var i = segments.Length - 2; i >= 0; i--)
            {
                if (currentNamespace.IsGlobalNamespace) return false;
                if (currentNamespace.Name != segments[i]) return false;
                currentNamespace = currentNamespace.ContainingNamespace;
            }

            return currentNamespace.IsGlobalNamespace;
        }
    }
}
