using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;

namespace GenerateRefAssemblySource
{
    internal static class PseudoCustomAttributeFacts
    {
        /* See https://github.com/dotnet/runtime/blob/v5.0.0-preview.6.20305.6/src/coreclr/src/System.Private.CoreLib/src/System/Reflection/CustomAttribute.cs#L1522-L1532
        FieldOffsetAttribute        field                           Don't know that it's needed for ref assembly. TODO for API tracking.
        SerializableAttribute       class, struct, enum, delegate   Synthesized by Roslyn
        MarshalAsAttribute          parameter, field, return-value  Not API
        ComImportAttribute          class, interface                Handled here. Essential for ref assembly because it determines whether you can omit the ref keyword when passing arguments to ref parameters..
        NonSerializedAttribute      field                           Don't know that it's needed for ref assembly. TODO for API tracking.
        InAttribute                 parameter                       Handled in syntax
        OutAttribute                parameter                       Handled in syntax
        OptionalAttribute           parameter                       Handled in syntax
        DllImportAttribute          method                          Not API
        PreserveSigAttribute        method                          Not API
        TypeForwardedToAttribute    assembly                        TODO

        Not a runtime pseudo-custom attribute but the C# language equivalent:
        MethodImplAttribute         method, constructor             Handled here. Needed to avoid warnings when using extern.
        */

        public static IEnumerable<AttributeData> GenerateApiAttributes(IAssemblySymbol assembly)
        {
            if (assembly.GetForwardedTypes().Any())
                throw new NotImplementedException("TODO: TypeForwardedToAttribute");

            yield break;
        }

        public static IEnumerable<AttributeData> GenerateApiAttributes(INamedTypeSymbol type)
        {
            if (type.IsComImport && TryCreateAttributeData(type, "System.Runtime.InteropServices.ComImportAttribute") is { } data)
                yield return data;
        }

        public static IEnumerable<AttributeData> GenerateApiAttributes(IMethodSymbol method)
        {
            var (options, codeType) = MetadataFacts.GetImplementationAttributes(method);
            if (options == 0 & codeType == 0) yield break;

            var attributeClass = MetadataFacts.GetFirstTypeAccessibleToAssembly(
                method.ContainingAssembly,
                "System.Runtime.CompilerServices.MethodImplAttribute");

            if (attributeClass is null) yield break;

            var attributeConstructor = attributeClass.InstanceConstructors.FirstOrDefault(c =>
                c.Parameters.Length == 1
                && c.Parameters[0].Type is { TypeKind: TypeKind.Enum } type
                && type.HasFullName("System", "Runtime", "CompilerServices", "MethodImplOptions"));

            if (attributeConstructor is null) yield break;

            var codeTypeEnum = MetadataFacts.GetFirstTypeAccessibleToAssembly(
                method.ContainingAssembly,
                "System.Runtime.CompilerServices.MethodCodeType");

            if (codeTypeEnum is null) yield break;

            var constructorArguments = ImmutableArray.Create(
                InternalAccessUtils.CreateTypedConstant(attributeConstructor.Parameters.Single().Type, TypedConstantKind.Enum, (int)options));

            var namedArguments = codeType == 0
                ? ImmutableArray<KeyValuePair<string, TypedConstant>>.Empty
                : ImmutableArray.Create(KeyValuePair.Create(
                    nameof(MethodImplAttribute.MethodCodeType),
                    InternalAccessUtils.CreateTypedConstant(codeTypeEnum, TypedConstantKind.Enum, (int)codeType)));

            yield return new SynthesizedAttributeData(attributeClass, attributeConstructor, constructorArguments, namedArguments);
        }

        public static IEnumerable<AttributeData> GenerateApiAttributes(ISymbol symbol)
        {
            return symbol switch
            {
                IAssemblySymbol assembly => GenerateApiAttributes(assembly),
                INamedTypeSymbol type => GenerateApiAttributes(type),
                IMethodSymbol method => GenerateApiAttributes(method),
                _ => Enumerable.Empty<AttributeData>(),
            };
        }

        private static AttributeData? TryCreateAttributeData(ISymbol symbol, string fullyQualifiedMetadataName)
        {
            var attributeClass = MetadataFacts.GetFirstTypeAccessibleToAssembly(
                symbol as IAssemblySymbol ?? symbol.ContainingAssembly,
                fullyQualifiedMetadataName);

            if (attributeClass is null) return null;

            var attributeConstructor = attributeClass.InstanceConstructors.FirstOrDefault(c => c.Parameters.IsEmpty);
            return new SynthesizedAttributeData(attributeClass, attributeConstructor);
        }

        private sealed class SynthesizedAttributeData : AttributeData
        {
            public SynthesizedAttributeData(
                INamedTypeSymbol? attributeClass,
                IMethodSymbol? attributeConstructor,
                ImmutableArray<TypedConstant> constructorArguments = default,
                ImmutableArray<KeyValuePair<string, TypedConstant>> namedArguments = default)
            {
                CommonAttributeClass = attributeClass;
                CommonAttributeConstructor = attributeConstructor;
                CommonConstructorArguments = constructorArguments.EmptyIfDefault();
                CommonNamedArguments = namedArguments.EmptyIfDefault();
            }

            protected override INamedTypeSymbol? CommonAttributeClass { get; }

            protected override IMethodSymbol? CommonAttributeConstructor { get; }

            protected override SyntaxReference? CommonApplicationSyntaxReference => null;

            protected override ImmutableArray<TypedConstant> CommonConstructorArguments { get; }

            protected override ImmutableArray<KeyValuePair<string, TypedConstant>> CommonNamedArguments { get; }
        }
    }
}
