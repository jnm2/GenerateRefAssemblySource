using Microsoft.CodeAnalysis;
using System;
using System.CodeDom.Compiler;

namespace GenerateRefAssemblySource
{
    internal readonly struct GenerationContext
    {
        public GenerationContext(IndentedTextWriter writer, INamespaceSymbol currentNamespace)
        {
            Writer = writer ?? throw new ArgumentNullException(nameof(writer));
            CurrentNamespace = currentNamespace;
        }

        public IndentedTextWriter Writer { get; }
        public INamespaceSymbol CurrentNamespace { get; }

        public bool IsInCurrentNamespace(INamedTypeSymbol type)
        {
            return SymbolEqualityComparer.Default.Equals(type.ContainingNamespace, CurrentNamespace);
        }

        public void WriteTypeReference(ITypeSymbol type)
        {
            if (type.SpecialType switch
            {
                SpecialType.System_Void => "void",
                SpecialType.System_Boolean => "bool",
                SpecialType.System_Byte => "byte",
                SpecialType.System_Char => "char",
                SpecialType.System_Decimal => "decimal",
                SpecialType.System_Double => "double",
                SpecialType.System_Int16 => "short",
                SpecialType.System_Int32 => "int",
                SpecialType.System_Int64 => "long",
                SpecialType.System_Object => "object",
                SpecialType.System_SByte => "sbyte",
                SpecialType.System_Single => "float",
                SpecialType.System_String => "string",
                SpecialType.System_UInt16 => "ushort",
                SpecialType.System_UInt32 => "uint",
                SpecialType.System_UInt64 => "ulong",
                _ => null,
            } is { } primitiveKeyword)
            {
                Writer.Write(primitiveKeyword);
            }
            else if (type is IArrayTypeSymbol array)
            {
                if (!array.IsSZArray) throw new NotImplementedException();

                WriteTypeReference(array.ElementType);
                Writer.Write("[]");
            }
            else if (type is IPointerTypeSymbol pointer)
            {
                WriteTypeReference(pointer.PointedAtType);
                Writer.Write('*');
            }
            else if (type is INamedTypeSymbol named)
            {
                if (type.ContainingType is { })
                {
                    WriteTypeReference(named.ContainingType);
                    Writer.Write('.');
                }
                else if (!type.ContainingNamespace.IsGlobalNamespace && !IsInCurrentNamespace(named))
                {
                    Writer.Write(type.ContainingNamespace.ToDisplayString());
                    Writer.Write('.');
                }

                Writer.Write(named.Name);

                if (named.IsGenericType)
                {
                    Writer.Write('<');

                    if (named.IsUnboundGenericType)
                    {
                        for (var i = 1; i < named.Arity; i++)
                            Writer.Write(',');
                    }
                    else
                    {
                        for (var i = 0; i < named.TypeArguments.Length; i++)
                        {
                            if (i != 0) Writer.Write(", ");
                            WriteTypeReference(named.TypeArguments[i]);
                        }
                    }

                    Writer.Write('>');
                }

                return;
            }
            else if (type is ITypeParameterSymbol)
            {
                Writer.Write(type.Name);
            }
            else
            {
                throw new NotImplementedException();
            }
        }
    }
}
