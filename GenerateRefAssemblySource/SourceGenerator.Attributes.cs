using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace GenerateRefAssemblySource
{
    partial class SourceGenerator
    {
        private void GenerateModuleAttributes(IAssemblySymbol assembly, IProjectFileSystem fileSystem)
        {
            if (assembly.Modules.Count() > 1 && assembly.Modules.SelectMany(m => m.GetAttributes()).Any())
                throw new NotImplementedException("Multiple modules with attributes");

            var moduleAttributes = assembly.Modules.Single().GetAttributes();

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

                context.Writer.Write('[');

                if (target is not null)
                {
                    context.Writer.Write(target);
                    context.Writer.Write(": ");
                }

                context.WriteTypeReference(attribute.AttributeClass!, asAttribute: true);

                if (attribute.ConstructorArguments.Any() || attribute.NamedArguments.Any())
                {
                    context.Writer.Write('(');

                    var isFirst = true;

                    foreach (var value in attribute.ConstructorArguments)
                    {
                        if (isFirst) isFirst = false; else context.Writer.Write(", ");
                        context.WriteTypedConstant(value);
                    }

                    foreach (var (name, value) in attribute.NamedArguments)
                    {
                        if (isFirst) isFirst = false; else context.Writer.Write(", ");

                        context.WriteIdentifier(name);
                        context.Writer.Write(" = ");
                        context.WriteTypedConstant(value);
                    }

                    context.Writer.Write(')');
                }

                context.Writer.WriteLine(']');
            }
        }
    }
}
