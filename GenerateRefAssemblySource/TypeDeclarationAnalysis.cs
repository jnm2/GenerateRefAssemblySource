using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace GenerateRefAssemblySource
{
    internal sealed class TypeDeclarationAnalysis
    {
        private readonly IAssemblySymbol assembly;
        private readonly Dictionary<INamedTypeSymbol, TypeDeclarationReason> reasonsByType = new (SymbolEqualityComparer.Default);
        private readonly HashSet<IMethodSymbol> usedAttributeConstructors = new(SymbolEqualityComparer.Default);

        public IReadOnlyDictionary<INamedTypeSymbol, TypeDeclarationReason> ReasonsByType => reasonsByType;

        public bool IsVisibleOrInOtherAssembly(INamedTypeSymbol type)
        {
            if (ReasonsByType.TryGetValue(type, out var reason))
                return reason.HasFlag(TypeDeclarationReason.ExternallyVisible);
            else
                return !assembly.Equals(type.ContainingAssembly, SymbolEqualityComparer.Default);
        }

        public bool IsUsedAttributeConstructor(IMethodSymbol method) => usedAttributeConstructors.Contains(method);

        public TypeDeclarationAnalysis(IAssemblySymbol assembly)
        {
            this.assembly = assembly;

            VisitAttributes(assembly.GetAttributes());

            foreach (var module in assembly.Modules)
                VisitAttributes(module.GetAttributes());

            VisitNamespace(assembly.GlobalNamespace);
        }

        private void VisitNamespace(INamespaceSymbol @namespace)
        {
            foreach (var nestedNamespace in @namespace.GetNamespaceMembers())
                VisitNamespace(nestedNamespace);

            foreach (var type in @namespace.GetTypeMembers())
            {
                if (type.DeclaredAccessibility == Accessibility.Public)
                    VisitNamedType(type, TypeDeclarationReason.ExternallyVisible);
            }
        }

        private void VisitNamedType(INamedTypeSymbol type, TypeDeclarationReason reason)
        {
            if (type.Kind == SymbolKind.ErrorType) return;
            if (!assembly.Equals(type.ContainingAssembly, SymbolEqualityComparer.Default)) return;

            var previousReason = reasonsByType.GetValueOrDefault(type);
            if (previousReason.HasFlag(reason)) return;
            reasonsByType[type] = previousReason | reason;

            if (reason != TypeDeclarationReason.ExternallyVisible) return;

            VisitAttributes(type.GetAttributes());
            VisitTypeParameters(type.TypeParameters);

            foreach (var member in type.GetMembers())
            {
                switch (member.DeclaredAccessibility)
                {
                    case Accessibility.Public:
                    case Accessibility.Protected or Accessibility.ProtectedOrInternal when MetadataFacts.IsInheritable(type):
                        break;

                    default:
                        continue;
                }

                switch (member)
                {
                    case INamedTypeSymbol nestedType:
                        VisitNamedType(nestedType, TypeDeclarationReason.ExternallyVisible);
                        break;

                    case IFieldSymbol field:
                        VisitAttributes(field.GetAttributes());
                        if (field.HasConstantValue)
                            VisitConstant(field.ConstantValue);
                        break;

                    case IEventSymbol @event:
                        VisitAttributes(@event.GetAttributes());
                        break;

                    case IPropertySymbol property:
                        VisitAttributes(property.GetAttributes());
                        VisitParameters(property.Parameters);
                        break;

                    case IMethodSymbol method:
                        if (method.MethodKind is MethodKind.Ordinary
                           or MethodKind.Constructor
                           or MethodKind.Conversion
                           or MethodKind.UserDefinedOperator
                           or MethodKind.DelegateInvoke)
                        {
                            VisitAttributes(method.GetAttributes());
                            VisitAttributes(method.GetReturnTypeAttributes());
                            VisitTypeParameters(method.TypeParameters);
                            VisitParameters(method.Parameters);
                        }
                        break;

                    default:
                        throw new NotImplementedException();
                }
            }
        }

        private void VisitTypeParameters(ImmutableArray<ITypeParameterSymbol> typeParameters)
        {
            foreach (var typeParameter in typeParameters)
                VisitAttributes(typeParameter.GetAttributes());
        }

        private void VisitParameters(ImmutableArray<IParameterSymbol> parameters)
        {
            foreach (var parameter in parameters)
            {
                VisitAttributes(parameter.GetAttributes());
                if (parameter.HasExplicitDefaultValue)
                    VisitConstant(parameter.ExplicitDefaultValue);
            }
        }

        private void VisitAttributes(ImmutableArray<AttributeData> attributes)
        {
            foreach (var attribute in attributes)
            {
                if (attribute.AttributeClass is not null)
                {
                    VisitNamedType(attribute.AttributeClass, TypeDeclarationReason.DeclaresUsedAttribute);

                    if (attribute.AttributeConstructor is not null
                        && usedAttributeConstructors.Add(attribute.AttributeConstructor))
                    {
                        foreach (var parameter in attribute.AttributeConstructor.Parameters)
                        {
                            if (parameter.HasExplicitDefaultValue)
                                VisitConstant(parameter.ExplicitDefaultValue);
                        }
                    }
                }

                foreach (var argument in attribute.ConstructorArguments)
                    VisitTypedConstant(argument);

                foreach (var (_, argument) in attribute.NamedArguments)
                    VisitTypedConstant(argument);
            }
        }

        private void VisitTypedConstant(TypedConstant typedConstant)
        {
            if (typedConstant.Kind == TypedConstantKind.Array)
            {
                foreach (var value in typedConstant.Values)
                    VisitTypedConstant(value);
            }
            else
            {
                VisitConstant(typedConstant.Value);
            }
        }

        private void VisitConstant(object? constant)
        {
            if (constant is ITypeSymbol type)
                VisitConstantType(type);
        }

        private void VisitConstantType(ITypeSymbol type)
        {
            switch (type)
            {
                case INamedTypeSymbol named:
                    VisitNamedType(named, TypeDeclarationReason.ReferencedInConstant);

                    foreach (var typeArgument in named.TypeArguments)
                        VisitConstantType(typeArgument);
                    break;

                case IArrayTypeSymbol array:
                    VisitConstantType(array.ElementType);
                    break;

                case IPointerTypeSymbol pointer:
                    VisitConstantType(pointer.PointedAtType);
                    break;

                case ITypeParameterSymbol or IDynamicTypeSymbol:
                    break;

                default:
                    throw new NotImplementedException();
            }
        }
    }
}
