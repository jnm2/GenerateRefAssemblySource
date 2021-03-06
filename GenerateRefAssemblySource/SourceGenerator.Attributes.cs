﻿using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace GenerateRefAssemblySource
{
    partial class SourceGenerator
    {
        private void GenerateModuleAttributes(IAssemblySymbol assembly, IProjectFileSystem fileSystem)
        {
            var moduleAttributes = assembly.Modules.SelectMany(m => m.GetAttributes()).ToImmutableArray();

            if (options.RemoveUnverifiableCodeAttribute)
            {
                moduleAttributes = moduleAttributes.RemoveAll(a =>
                    a.AttributeClass is not null
                    && a.AttributeClass.HasFullName("System", "Security", "UnverifiableCodeAttribute"));
            }

            GenerateAttributesFile(fileSystem, "Properties/ModuleInfo.cs", moduleAttributes, "module");
        }

        private void GenerateAssemblyAttributes(IAssemblySymbol assembly, IProjectFileSystem fileSystem)
        {
            var assemblyAttributes = assembly.GetAttributes()
                .AddRange(PseudoCustomAttributeFacts.GenerateApiAttributes(assembly));

            if (options.RemoveAssemblySigningAttributes)
            {
                assemblyAttributes = assemblyAttributes.RemoveAll(a =>
                    a.AttributeClass?.Name is
                        "AssemblyDelaySignAttribute"
                        or "AssemblyKeyFileAttribute"
                        or "AssemblyKeyNameAttribute"
                        or "AssemblySignatureKeyAttribute"
                    && a.AttributeClass.ContainingNamespace.HasFullName("System", "Reflection"));
            }

            GenerateAttributesFile(fileSystem, "Properties/AssemblyInfo.cs", assemblyAttributes, "assembly", initialLines: ImmutableArray.Create(
                "[assembly: System.Runtime.CompilerServices.ReferenceAssembly]",
                $"[assembly: System.Reflection.AssemblyVersion(\"{assembly.Identity.Version}\")]"));

            if (!MetadataFacts.CanAccessType(assembly, "System.Runtime.CompilerServices.ReferenceAssemblyAttribute"))
            {
                fileSystem.WriteAllLines(
                    "System/Runtime/CompilerServices/ReferenceAssemblyAttribute.cs",
                    "namespace System.Runtime.CompilerServices",
                    "{",
                    "    internal sealed class ReferenceAssemblyAttribute : Attribute { }",
                    "}");
            }
        }

        private static void GenerateAttributesFile(
            IProjectFileSystem fileSystem,
            string fileName,
            ImmutableArray<AttributeData> attributes,
            string target,
            ImmutableArray<string> initialLines = default)
        {
            if (attributes.IsEmpty && initialLines.IsDefaultOrEmpty) return;

            using var textWriter = fileSystem.CreateText(fileName);
            using var writer = new IndentedTextWriter(textWriter);

            if (!initialLines.IsDefaultOrEmpty)
            {
                foreach (var line in initialLines)
                    writer.WriteLine(line);

                if (!attributes.IsEmpty)
                    writer.WriteLine();
            }

            WriteAttributes(attributes, target, new GenerationContext(writer, currentNamespace: null));
        }

        private static void WriteAttributes(IEnumerable<AttributeData> attributes, string? target, GenerationContext context, bool onlyWriteAttributeUsageAttribute = false)
        {
            foreach (var attribute in attributes.OrderBy(a => a.AttributeClass, NamespaceOrTypeFullNameComparer.Instance))
            {
                if (onlyWriteAttributeUsageAttribute && !MetadataFacts.IsAttributeUsageAttribute(attribute.AttributeClass))
                {
                    continue;
                }

                var buffer = new StringWriter();
                var bufferedWriter = new IndentedTextWriter(buffer);
                var bufferedContext = context.WithWriter(bufferedWriter);

                bufferedContext.Writer.Write('[');

                if (target is not null)
                {
                    bufferedContext.Writer.Write(target);
                    bufferedContext.Writer.Write(": ");
                }

                bufferedContext.WriteTypeReference(attribute.AttributeClass!, asAttribute: true);

                if (attribute.ConstructorArguments.Any() || attribute.NamedArguments.Any())
                {
                    bufferedContext.Writer.Write('(');

                    for (var i = 0; i < attribute.ConstructorArguments.Length; i++)
                    {
                        if (i != 0) bufferedContext.Writer.Write(", ");

                        var value = attribute.ConstructorArguments[i];

                        if (value.Kind == TypedConstantKind.Primitive
                            && value.Value is 0 or (short)0 or (ushort)0 or (byte)0 or (sbyte)0
                            && attribute.AttributeClass!.InstanceConstructors.Any(c =>
                                c.Parameters.ElementAtOrDefault(i) is { } p
                                && CanImplicitlyConvertFromZeroLiteralSyntax(p.Type)
                                && !SymbolEqualityComparer.Default.Equals(p.Type, value.Type)))
                        {
                            bufferedContext.Writer.Write('(');
                            bufferedContext.WriteTypeReference(value.Type);
                            bufferedContext.Writer.Write(')');
                        }

                        bufferedContext.WriteTypedConstant(value);
                    }

                    var isFirst = attribute.ConstructorArguments.IsEmpty;

                    foreach (var (name, value) in attribute.NamedArguments)
                    {
                        if (isFirst) isFirst = false; else bufferedContext.Writer.Write(", ");

                        bufferedContext.WriteIdentifier(name);
                        bufferedContext.Writer.Write(" = ");
                        bufferedContext.WriteTypedConstant(value);
                    }

                    bufferedContext.Writer.Write(')');
                }

                bufferedContext.Writer.WriteLine(']');

                var bufferedText = buffer.ToString();
                if (bufferedText.Contains(GenerationContext.ErrorText))
                    context.WriteComment(bufferedText);
                else
                    context.Writer.Write(bufferedText);
            }
        }

        private static bool CanImplicitlyConvertFromZeroLiteralSyntax(ITypeSymbol type)
        {
            return type.TypeKind == TypeKind.Enum || type.SpecialType is
                SpecialType.System_Int32
                or SpecialType.System_UInt32
                or SpecialType.System_Int64
                or SpecialType.System_UInt64
                or SpecialType.System_Int16
                or SpecialType.System_UInt16
                or SpecialType.System_Byte
                or SpecialType.System_SByte
                or SpecialType.System_Single
                or SpecialType.System_Double
                or SpecialType.System_Decimal
                or SpecialType.System_Char;
        }
    }
}
