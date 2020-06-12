using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace GenerateRefAssemblySource
{
    public static class Program
    {
        private static readonly ImmutableArray<TypeMemberSortKind> TypeMemberOrder = ImmutableArray.Create(
            TypeMemberSortKind.Constant,
            TypeMemberSortKind.Field,
            TypeMemberSortKind.Constructor,
            TypeMemberSortKind.Property,
            TypeMemberSortKind.Indexer,
            TypeMemberSortKind.Event,
            TypeMemberSortKind.Method,
            TypeMemberSortKind.Operator,
            TypeMemberSortKind.Conversion);

        public static void Main(string[] args)
        {
            var sourceFolder = args.Single();
            var outputDirectory = Directory.GetCurrentDirectory();

            var dllFilePaths = Directory.GetFiles(sourceFolder, "*.dll");

            var compilation = CSharpCompilation.Create(
                assemblyName: string.Empty,
                syntaxTrees: null,
                dllFilePaths.Select(path => MetadataReference.CreateFromFile(path)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, metadataImportOptions: MetadataImportOptions.Public));

            foreach (var reference in compilation.References)
            {
                var assembly = (IAssemblySymbol?)compilation.GetAssemblyOrModuleSymbol(reference);
                if (assembly is null) continue;

                VisitAssembly(assembly, new ProjectFileSystem(Path.Join(outputDirectory, assembly.Name)));
            }
        }

        private static void VisitAssembly(IAssemblySymbol assembly, IProjectFileSystem fileSystem)
        {
            VisitNamespace(assembly.GlobalNamespace, fileSystem);
        }

        private static void VisitNamespace(INamespaceSymbol @namespace, IProjectFileSystem fileSystem)
        {
            foreach (var type in @namespace.GetTypeMembers())
            {
                if (type.DeclaredAccessibility == Accessibility.Public)
                    VisitType(type, fileSystem);
            }

            foreach (var containedNamespace in @namespace.GetNamespaceMembers())
            {
                VisitNamespace(containedNamespace, fileSystem);
            }
        }

        private static void VisitType(INamedTypeSymbol type, IProjectFileSystem fileSystem)
        {
            var externallyVisibleContainedTypes = type.GetTypeMembers().RemoveAll(t => !MetadataFacts.IsVisibleOutsideAssembly(t));

            GenerateType(type, fileSystem, declareAsPartial: externallyVisibleContainedTypes.Any());

            foreach (var containedType in externallyVisibleContainedTypes)
            {
                VisitType(containedType, fileSystem);
            }
        }

        private static void GenerateType(INamedTypeSymbol type, IProjectFileSystem fileSystem, bool declareAsPartial)
        {
            using var textWriter = fileSystem.Create(GetPathForType(type));
            using var writer = new IndentedTextWriter(textWriter);

            if (!type.ContainingNamespace.IsGlobalNamespace)
            {
                writer.Write("namespace ");
                writer.WriteLine(type.ContainingNamespace.ToDisplayString());
                writer.WriteLine('{');
                writer.Indent++;
            }

            var context = new GenerationContext(writer, type.ContainingNamespace);

            var containingTypes = MetadataFacts.GetContainingTypes(type);
            foreach (var containingType in containingTypes)
            {
                WriteContainerTypeHeader(containingType, declareAsPartial: true, context);
                writer.WriteLine();
                writer.WriteLine('{');
                writer.Indent++;
            }

            WriteAccessibility(type.DeclaredAccessibility, writer);

            if (type.TypeKind == TypeKind.Delegate)
            {
                GenerateDelegate(type, context);
            }
            else if (type.TypeKind == TypeKind.Enum)
            {
                if (declareAsPartial) writer.Write("partial ");
                GenerateEnum(type, context);
            }
            else
            {
                if (type.TypeKind == TypeKind.Class)
                {
                    if (type.IsAbstract)
                        writer.Write(type.IsSealed ? "static " : "abstract ");
                    else if (type.IsSealed)
                        writer.Write("sealed ");
                }

                WriteContainerTypeHeader(type, declareAsPartial, context);

                var baseTypes = new List<INamedTypeSymbol>();

                if (type.BaseType is { SpecialType: not (SpecialType.System_Object or SpecialType.System_ValueType) })
                    baseTypes.Add(type.BaseType);

                baseTypes.AddRange(type.Interfaces.Where(i => MetadataFacts.IsVisibleOutsideAssembly(i)));

                WriteBaseTypes(MetadataFacts.RemoveBaseTypes(baseTypes), context);

                WriteGenericParameterConstraints(type.TypeParameters, context);

                writer.WriteLine();
                writer.WriteLine('{');
                writer.Indent++;

                WriteTypeMembers(type, context);

                writer.Indent--;
                writer.WriteLine('}');
            }

            foreach (var containingType in containingTypes)
            {
                writer.Indent--;
                writer.WriteLine('}');
            }

            if (!type.ContainingNamespace.IsGlobalNamespace)
            {
                writer.Indent--;
                writer.WriteLine('}');
            }
        }

        private static void WriteTypeMembers(INamedTypeSymbol type, GenerationContext context)
        {
            var isFirst = true;

            foreach (var (member, sortKind) in type.GetMembers()
                .Where(MetadataFacts.IsVisibleOutsideAssembly)
                .Select(m => (Member: m, SortKind: MetadataFacts.GetTypeMemberSortKind(m)))
                .Where(m => m.SortKind is not null)
                .OrderBy(m => TypeMemberOrder.IndexOf(m.SortKind!.Value))
                .ThenByDescending(m => m.Member.DeclaredAccessibility == Accessibility.Public)
                .ThenByDescending(m => m.Member.IsStatic)
                .ThenByDescending(m => (m.Member as IFieldSymbol)?.IsReadOnly)
                .ThenBy(m => m.Member.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (isFirst) isFirst = false; else context.Writer.WriteLine();

                switch (sortKind!.Value)
                {
                    default:
                        context.Writer.Write("// TODO: ");
                        context.Writer.Write(sortKind!.Value);
                        context.Writer.Write(' ');
                        context.Writer.WriteLine(member.Name);
                        break;
                }
            }
        }

        private static void WriteContainerTypeHeader(INamedTypeSymbol type, bool declareAsPartial, GenerationContext context)
        {
            if (declareAsPartial) context.Writer.Write("partial ");

            context.Writer.Write(type.TypeKind switch
            {
                TypeKind.Enum => "enum ",
                TypeKind.Struct => "struct ",
                TypeKind.Interface => "interface ",
                TypeKind.Class => "class ",
            });

            context.Writer.Write(type.Name);
            WriteGenericParameterList(type, context.Writer);
        }

        private static void WriteBaseTypes(IReadOnlyCollection<INamedTypeSymbol> baseTypes, GenerationContext context)
        {
            using var enumerator = baseTypes
                .OrderByDescending(context.IsInCurrentNamespace)
                .ThenBy(t => t, NamespaceOrTypeFullNameComparer.Instance)
                .GetEnumerator();

            if (!enumerator.MoveNext()) return;

            context.Writer.Write(" :");

            var multiline = baseTypes.Count > 3;
            if (multiline) context.Writer.Indent++;

            while (true)
            {
                if (multiline)
                    context.Writer.WriteLine();
                else
                    context.Writer.Write(' ');

                context.WriteTypeReference(enumerator.Current);
                if (!enumerator.MoveNext()) break;
                context.Writer.Write(',');
            }

            if (multiline) context.Writer.Indent--;
        }

        private static void GenerateDelegate(INamedTypeSymbol type, GenerationContext context)
        {
            context.Writer.Write("delegate ");
            context.WriteTypeReference(type.DelegateInvokeMethod!.ReturnType);
            context.Writer.Write(' ');
            context.Writer.Write(type.Name);

            WriteGenericParameterList(type, context.Writer);
            WriteParameterList(type.DelegateInvokeMethod, context);
            WriteGenericParameterConstraints(type.TypeParameters, context);

            context.Writer.WriteLine(';');
        }

        private static void GenerateEnum(INamedTypeSymbol type, GenerationContext context)
        {
            context.Writer.Write("enum ");
            context.Writer.Write(type.Name);

            if (type.EnumUnderlyingType!.SpecialType != SpecialType.System_Int32)
            {
                context.Writer.Write(" : ");
                context.WriteTypeReference(type.EnumUnderlyingType);
            }

            context.Writer.WriteLine();
            context.Writer.WriteLine('{');
            context.Writer.Indent++;

            foreach (var field in type.GetMembers().OfType<IFieldSymbol>()
                .Where(f => f.HasConstantValue)
                .OrderBy(f => f.ConstantValue)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                context.Writer.Write(field.Name);
                context.Writer.Write(" = ");
                context.Writer.Write(field.ConstantValue);
                context.Writer.WriteLine(',');
            }

            context.Writer.Indent--;
            context.Writer.WriteLine('}');
        }

        private static void WriteGenericParameterList(INamedTypeSymbol type, TextWriter writer)
        {
            if (!type.TypeParameters.Any()) return;

            writer.Write('<');

            for (var i = 0; i < type.TypeParameters.Length; i++)
            {
                if (i != 0) writer.Write(", ");

                var genericParameter = type.TypeParameters[i];

                writer.Write(genericParameter.Variance switch
                {
                    VarianceKind.In => "in ",
                    VarianceKind.Out => "out ",
                    _ => null,
                });

                writer.Write(genericParameter.Name);
            }

            writer.Write('>');
        }

        private static void WriteGenericParameterConstraints(ImmutableArray<ITypeParameterSymbol> typeParameters, GenerationContext context)
        {
            context.Writer.Indent++;

            foreach (var typeParameter in typeParameters)
            {
                var mutuallyExclusiveInitialConstraintKeyword = new[]
                {
                    (Condition: typeParameter.HasReferenceTypeConstraint, Keyword: "class"),
                    (Condition: typeParameter.HasValueTypeConstraint, Keyword: "struct"),
                    (Condition: typeParameter.HasNotNullConstraint, Keyword: "notnull"),
                    (Condition: typeParameter.HasUnmanagedTypeConstraint, Keyword: "unmanaged"),
                }.SingleOrDefault(t => t.Condition).Keyword;

                if (mutuallyExclusiveInitialConstraintKeyword is null
                    && !typeParameter.HasConstructorConstraint
                    && !typeParameter.ConstraintTypes.Any())
                {
                    continue;
                }

                context.Writer.WriteLine();
                context.Writer.Write("where ");
                context.Writer.Write(typeParameter.Name);
                context.Writer.Write(" : ");

                if (mutuallyExclusiveInitialConstraintKeyword is { })
                    context.Writer.Write(mutuallyExclusiveInitialConstraintKeyword);

                var isFirst = mutuallyExclusiveInitialConstraintKeyword is null;

                for (var i = 0; i < typeParameter.ConstraintTypes.Length; i++)
                {
                    if (isFirst) context.Writer.Write(", "); else isFirst = false;
                    context.WriteTypeReference(typeParameter.ConstraintTypes[i]);
                }

                if (typeParameter.HasConstructorConstraint)
                {
                    if (isFirst) context.Writer.Write(", ");
                    context.Writer.Write("new()");
                }
            }

            context.Writer.Indent--;
        }

        private static void WriteParameterList(IMethodSymbol method, GenerationContext context)
        {
            context.Writer.Write('(');

            for (var i = 0; i < method.Parameters.Length; i++)
            {
                if (i != 0) context.Writer.Write(", ");
                var parameter = method.Parameters[i];

                context.WriteTypeReference(parameter.Type);
                context.Writer.Write(' ');
                context.Writer.Write(parameter.Name);
            }

            context.Writer.Write(')');
        }

        private static void WriteAccessibility(Accessibility accessibility, TextWriter writer)
        {
            writer.Write(accessibility switch
            {
                Accessibility.Public => "public ",
                Accessibility.Protected => "protected ",
                Accessibility.ProtectedOrInternal => "protected "
            });
        }

        private static string GetPathForType(INamedTypeSymbol type)
        {
            var typeAndContainingTypes = new List<INamedTypeSymbol>();

            for (var current = type; current is { }; current = current.ContainingType)
                typeAndContainingTypes.Add(current);

            typeAndContainingTypes.Reverse();

            var filename = new StringBuilder();

            foreach (var typeOrContainingType in typeAndContainingTypes)
            {
                filename.Append(typeOrContainingType.Name);

                if (typeOrContainingType.TypeParameters.Any())
                {
                    filename.Append('{');
                    filename.AppendJoin(',', typeOrContainingType.TypeParameters.Select(p => p.Name));
                    filename.Append('}');
                }

                filename.Append('.');
            }

            filename.Append("cs");

            return Path.Join(type.ContainingNamespace.Name?.Replace('.', Path.DirectorySeparatorChar), filename.ToString());
        }
    }
}
