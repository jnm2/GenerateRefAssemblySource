using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace GenerateRefAssemblySource
{
    partial class SourceGenerator
    {
        private void GenerateModuleAttributes(IAssemblySymbol assembly, IProjectFileSystem fileSystem)
        {
            var moduleAttributes = assembly.Modules
                .SelectMany(m => m.GetAttributes())
                .Where(a => !(
                    options.RemoveUnverifiableCodeAttribute
                    && a.AttributeClass is not null
                    && a.AttributeClass.HasFullName("System", "Security", "UnverifiableCodeAttribute")))
                .ToImmutableArray();

            if (moduleAttributes.Any())
            {
                if (assembly.Modules.Count() > 1)
                    throw new NotImplementedException("Multiple modules with attributes");

                GenerateAttributesFile(fileSystem, moduleAttributes, "Properties/ModuleInfo.cs", "module");
            }
        }

        private void GenerateAssemblyAttributes(IAssemblySymbol assembly, IProjectFileSystem fileSystem)
        {
            var assemblyAttributes = assembly.GetAttributes()
                .Where(a => !(
                    options.RemoveAssemblySigningAttributes
                    && a.AttributeClass?.Name is
                        "AssemblyDelaySignAttribute"
                        or "AssemblyKeyFileAttribute"
                        or "AssemblyKeyNameAttribute"
                        or "AssemblySignatureKeyAttribute"
                    && a.AttributeClass.ContainingNamespace.HasFullName("System", "Reflection")))
                .ToImmutableArray();

            if (assemblyAttributes.Any())
                GenerateAttributesFile(fileSystem, assemblyAttributes, "Properties/AssemblyInfo.cs", "assembly");
        }

        private static void GenerateAttributesFile(IProjectFileSystem fileSystem, ImmutableArray<AttributeData> attributes, string fileName, string target)
        {
            using var textWriter = fileSystem.CreateText(fileName);
            using var writer = new IndentedTextWriter(textWriter);
            var context = new GenerationContext(writer, currentNamespace: null);

            foreach (var attribute in attributes.OrderBy(a => a.AttributeClass, NamespaceOrTypeFullNameComparer.Instance))
            {
                writer.Write('[');
                writer.Write(target);
                writer.Write(": ");
                context.WriteTypeReference(attribute.AttributeClass!, asAttribute: true);

                if (attribute.ConstructorArguments.Any() || attribute.NamedArguments.Any())
                {
                    writer.Write('(');

                    var isFirst = true;

                    foreach (var value in attribute.ConstructorArguments)
                    {
                        if (isFirst) isFirst = false; else writer.Write(", ");
                        context.WriteTypedConstant(value);
                    }

                    foreach (var (name, value) in attribute.NamedArguments)
                    {
                        if (isFirst) isFirst = false; else writer.Write(", ");

                        context.WriteIdentifier(name);
                        writer.Write(" = ");
                        context.WriteTypedConstant(value);
                    }

                    writer.Write(')');
                }

                writer.WriteLine(']');
            }
        }
    }
}
