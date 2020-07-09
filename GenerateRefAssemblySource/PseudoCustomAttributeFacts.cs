using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

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
