using System;
using System.CodeDom.Compiler;

namespace GenerateRefAssemblySource
{
    internal readonly struct GenerationContext
    {
        public GenerationContext(IndentedTextWriter writer, string? currentNamespace = null, Type[]? genericTypeParameters = null, Type[]? genericMethodParameters = null)
        {
            Writer = writer ?? throw new ArgumentNullException(nameof(writer));
            CurrentNamespace = currentNamespace;
            GenericTypeParameters = genericTypeParameters ?? Type.EmptyTypes;
            GenericMethodParameters = genericMethodParameters ?? Type.EmptyTypes;
        }

        public IndentedTextWriter Writer { get; }

        public string? CurrentNamespace { get; }

        public Type[] GenericTypeParameters { get; }

        public Type[] GenericMethodParameters { get; }

        public GenerationContext WithCurrentNamespace(string? currentNamespace)
        {
            return new GenerationContext(Writer, currentNamespace, GenericTypeParameters, GenericMethodParameters);
        }

        public GenerationContext WithGenericTypeParameters(Type[] genericTypeParameters)
        {
            return new GenerationContext(Writer, CurrentNamespace, genericTypeParameters, GenericMethodParameters);
        }

        public GenerationContext WithGenericMethodParameters(Type[] genericMethodParameters)
        {
            return new GenerationContext(Writer, CurrentNamespace, GenericTypeParameters, genericMethodParameters);
        }

        public void WriteTypeReference(Type type)
        {
            if (type.IsGenericTypeParameter)
            {
                Writer.Write(GenericTypeParameters[type.GenericParameterPosition].Name);
            }
            else if (type.IsGenericMethodParameter)
            {
                Writer.Write(GenericMethodParameters[type.GenericParameterPosition].Name);
            }
            else if (type.IsGenericType)
            {
                if (!string.IsNullOrEmpty(type.Namespace) && type.Namespace != CurrentNamespace)
                {
                    Writer.Write(type.Namespace);
                    Writer.Write('.');
                }

                Writer.Write(MetadataFacts.ParseTypeName(type.Name).Name);
                Writer.Write('<');

                var genericArguments = type.GetGenericArguments();

                if (type.IsConstructedGenericType)
                {
                    for (var i = 0; i < genericArguments.Length; i++)
                    {
                        if (i != 0) Writer.Write(", ");
                        WriteTypeReference(genericArguments[i]);
                    }
                }
                else
                {
                    for (var i = 1; i < genericArguments.Length; i++)
                        Writer.Write(',');
                }

                Writer.Write('>');
            }
            else
            {
                Writer.Write(type.FullName switch
                {
                    "System.Void" => "void",
                    "System.Boolean" => "bool",
                    "System.Byte" => "byte",
                    "System.Char" => "char",
                    "System.Decimal" => "decimal",
                    "System.Double" => "double",
                    "System.Int16" => "short",
                    "System.Int32" => "int",
                    "System.Int64" => "long",
                    "System.Object" => "object",
                    "System.SByte" => "sbyte",
                    "System.Single" => "float",
                    "System.String" => "string",
                    "System.UInt16" => "ushort",
                    "System.UInt32" => "uint",
                    "System.UInt64" => "ulong",
                    _ => (type.Namespace == CurrentNamespace ? type.Name : type.FullName)
                        ?? throw new NotImplementedException("Missing type name"),
                });
            }
        }
    }
}
