using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace GenerateRefAssemblySource
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var outputDirectory = Path.GetFullPath(".");

            foreach (var filePath in args)
            {
                var filesInSameDirectory = Directory.GetFiles(Path.GetDirectoryName(filePath), "*.dll");
                using var context = new MetadataLoadContext(new PathAssemblyResolver(filesInSameDirectory));

                GenerateAssembly(
                    context.LoadFromAssemblyPath(filePath),
                    new ProjectFileSystem(Path.Join(outputDirectory, context.LoadFromAssemblyPath(filePath).GetName().Name)));
            }
        }

        private static void GenerateAssembly(Assembly assembly, IProjectFileSystem fileSystem)
        {
            foreach (var externallyVisibleType in assembly.GetExportedTypes())
            {
                GenerateType(externallyVisibleType, fileSystem);
            }
        }

        private static void GenerateType(Type type, IProjectFileSystem fileSystem)
        {
            using var textWriter = fileSystem.Create(GetPathForType(type));
            using var writer = new IndentedTextWriter(textWriter);

            if (!string.IsNullOrEmpty(type.Namespace))
            {
                writer.Write("namespace ");
                writer.WriteLine(type.Namespace);
                writer.WriteLine('{');
                writer.Indent++;
            }

            var context = new GenerationContext(writer, type.Namespace);

            var containingTypes = MetadataFacts.GetContainingTypes(type);
            foreach (var containingType in containingTypes)
            {
                context = context.WithGenericTypeParameters(containingType.GetGenericArguments());
                WriteContainerTypeHeader(containingType, context);
                writer.WriteLine('{');
                writer.Indent++;
            }

            context = context.WithGenericTypeParameters(type.GetGenericArguments());

            writer.Write("public ");

            var hasExportedNestedTypes = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Any(MetadataFacts.IsVisibleOutsideAssembly);

            if (type.IsClass && type.BaseType?.FullName == "System.MulticastDelegate")
            {
                GenerateDelegate(type, context);
            }
            else if (type.IsEnum)
            {
                if (hasExportedNestedTypes) writer.Write("partial ");
                GenerateEnum(type, context);
            }
            else
            {
                if (type.IsInterface)
                {
                    if (hasExportedNestedTypes) writer.Write("partial ");
                    writer.Write("interface ");
                }
                else if (type.IsClass)
                {
                    if (type.IsAbstract)
                        writer.Write(type.IsSealed ? "static " : "abstract ");
                    else if (type.IsSealed)
                        writer.Write("sealed ");

                    if (hasExportedNestedTypes) writer.Write("partial ");
                    writer.Write("class ");
                }
                else
                {
                    if (hasExportedNestedTypes) writer.Write("partial ");
                    writer.Write("struct ");
                }

                writer.Write(MetadataFacts.ParseTypeName(type.Name).Name);
                var genericParameters = MetadataFacts.GetNewGenericTypeParameters(type);
                WriteGenericParameterList(genericParameters, context.Writer);

                var baseTypes = new List<Type>();

                if (type.BaseType is { } && type.IsClass && type.BaseType.FullName != "System.Object")
                    baseTypes.Add(type.BaseType);

                baseTypes.AddRange(type.GetInterfaces().Where(i => i.IsVisible));

                WriteBaseTypes(baseTypes, context);

                WriteGenericParameterConstraints(genericParameters, context);

                writer.WriteLine();
                writer.WriteLine('{');
                writer.WriteLine('}');
            }

            foreach (var containingType in containingTypes)
            {
                writer.Indent--;
                writer.WriteLine('}');
            }

            if (!string.IsNullOrEmpty(type.Namespace))
            {
                writer.Indent--;
                writer.WriteLine('}');
            }
        }

        private static void WriteContainerTypeHeader(Type type, GenerationContext context)
        {
            context.Writer.Write("partial ");

            if (type.IsEnum)
                context.Writer.Write("enum ");
            else if (type.IsValueType)
                context.Writer.Write("struct ");
            else if (type.IsInterface)
                context.Writer.Write("interface ");
            else
                context.Writer.Write("class ");

            context.Writer.Write(MetadataFacts.ParseTypeName(type.Name).Name);
            WriteGenericParameterList(MetadataFacts.GetNewGenericTypeParameters(type), context.Writer);

            context.Writer.WriteLine();
        }

        private static void WriteBaseTypes(IEnumerable<Type> baseTypes, GenerationContext context)
        {
            using var enumerator = baseTypes.GetEnumerator();

            if (!enumerator.MoveNext()) return;

            context.Writer.Write(" : ");

            while (true)
            {
                context.WriteTypeReference(enumerator.Current);
                if (!enumerator.MoveNext()) break;
                context.Writer.Write(", ");
            }
        }

        private static void GenerateDelegate(Type type, GenerationContext context)
        {
            var invokeMethod = type.GetMethod("Invoke") ?? throw new NotImplementedException("No Invoke method found.");

            context.Writer.Write("delegate ");
            context.WriteTypeReference(invokeMethod.ReturnType);
            context.Writer.Write(' ');
            context.Writer.Write(MetadataFacts.ParseTypeName(type.Name).Name);

            var genericParameters = MetadataFacts.GetNewGenericTypeParameters(type);
            WriteGenericParameterList(genericParameters, context.Writer);
            WriteParameterList(invokeMethod.GetParameters(), context);
            WriteGenericParameterConstraints(genericParameters, context);

            context.Writer.WriteLine(';');
        }

        private static void GenerateEnum(Type type, GenerationContext context)
        {
            context.Writer.Write("enum ");
            context.Writer.Write(type.Name);

            var underlyingType = type.GetEnumUnderlyingType();
            if (underlyingType.FullName != "System.Int32")
            {
                context.Writer.Write(" : ");
                context.WriteTypeReference(underlyingType);
            }

            context.Writer.WriteLine();
            context.Writer.WriteLine('{');
            context.Writer.Indent++;

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static)
                .OrderBy(f => f.GetRawConstantValue())
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                context.Writer.Write(field.Name);
                context.Writer.Write(" = ");
                context.Writer.Write(field.GetRawConstantValue());
                context.Writer.WriteLine(',');
            }

            context.Writer.Indent--;
            context.Writer.WriteLine('}');
        }

        private static void WriteGenericParameterList(ImmutableArray<Type> genericParameters, TextWriter writer)
        {
            if (!genericParameters.Any()) return;

            writer.Write('<');

            for (var i = 0; i < genericParameters.Length; i++)
            {
                if (i != 0) writer.Write(", ");

                var genericParameter = genericParameters[i];

                writer.Write((genericParameter.GenericParameterAttributes & GenericParameterAttributes.VarianceMask) switch
                {
                    GenericParameterAttributes.Covariant => "out ",
                    GenericParameterAttributes.Contravariant => "in ",
                    0 => null,
                    _ => throw new NotSupportedException("Not representable in C#"),
                });

                writer.Write(genericParameter.Name);
            }

            writer.Write('>');
        }

        private static void WriteGenericParameterConstraints(ImmutableArray<Type> genericParameters, GenerationContext context)
        {
            context.Writer.Indent++;

            foreach (var parameter in genericParameters)
            {
                const GenericParameterAttributes constraintAttributes = GenericParameterAttributes.NotNullableValueTypeConstraint | GenericParameterAttributes.ReferenceTypeConstraint | GenericParameterAttributes.DefaultConstructorConstraint;

                var constraints = parameter.GetGenericParameterConstraints();

                if ((parameter.GenericParameterAttributes & constraintAttributes) == 0 && !constraints.Any())
                {
                    continue;
                }

                context.Writer.WriteLine();
                context.Writer.Write("where ");
                context.Writer.Write(parameter.Name);
                context.Writer.Write(" : ");

                var isFirst = false;

                var hasStructConstraint = (parameter.GenericParameterAttributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
                var hasDefaultConstructorConstraint = (parameter.GenericParameterAttributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0;

                if (hasStructConstraint)
                {
                    if (!hasDefaultConstructorConstraint) throw new NotSupportedException("Not representable in C#");

                    context.Writer.Write("struct");
                    isFirst = false;
                }

                if ((parameter.GenericParameterAttributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                {
                    context.Writer.Write("class");
                    isFirst = false;
                }

                for (var i = 0; i < constraints.Length; i++)
                {
                    if (isFirst) context.Writer.Write(", "); else isFirst = false;
                    context.WriteTypeReference(constraints[i]);
                }

                if (hasDefaultConstructorConstraint && !hasStructConstraint)
                {
                    if (isFirst) context.Writer.Write(", ");
                    context.Writer.Write("new()");
                }
            }

            context.Writer.Indent--;
        }

        private static void WriteParameterList(ParameterInfo[] parameters, GenerationContext context)
        {
            context.Writer.Write('(');

            for (var i = 0; i < parameters.Length; i++)
            {
                if (i != 0) context.Writer.Write(", ");
                var parameter = parameters[i];

                context.WriteTypeReference(parameter.ParameterType);
                context.Writer.Write(' ');
                context.Writer.Write(parameter.Name);
            }

            context.Writer.Write(')');
        }

        private static string GetPathForType(Type type)
        {
            var typeAndDeclaringTypes = new List<Type>();

            for (var current = type; current is { }; current = current.DeclaringType)
                typeAndDeclaringTypes.Add(current);

            typeAndDeclaringTypes.Reverse();

            var filename = new StringBuilder();

            var previouslyDeclaredGenericParameterCount = 0;

            foreach (var typeOrDeclaringType in typeAndDeclaringTypes)
            {
                if (typeOrDeclaringType.IsGenericType
                    && type.GetGenericArguments() is var genericParameters
                    && genericParameters.Length > previouslyDeclaredGenericParameterCount)
                {
                    var (name, arity) = MetadataFacts.ParseTypeName(typeOrDeclaringType.Name);

                    if (arity != genericParameters.Length - previouslyDeclaredGenericParameterCount)
                        throw new NotImplementedException("C# cannot declare a generic type without the metadata name ending with a backtick and the generic arity.");

                    filename.Append(name);
                    filename.Append('{');
                    filename.AppendJoin(',', genericParameters.Select(p => p.Name));
                    filename.Append('}');

                    previouslyDeclaredGenericParameterCount = genericParameters.Length;
                }
                else
                {
                    filename.Append(typeOrDeclaringType.Name);
                }

                filename.Append('.');
            }

            filename.Append("cs");

            return Path.Join(type.Namespace?.Replace('.', Path.DirectorySeparatorChar), filename.ToString());
        }
    }
}
