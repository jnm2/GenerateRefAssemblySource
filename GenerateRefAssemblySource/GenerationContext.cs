using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenerateRefAssemblySource
{
    internal readonly struct GenerationContext
    {
        public GenerationContext(IndentedTextWriter writer, INamespaceSymbol? currentNamespace, bool isDefiningPrimitiveTypeConstant = false)
        {
            Writer = writer ?? throw new ArgumentNullException(nameof(writer));
            CurrentNamespace = currentNamespace;
            IsDefiningPrimitiveTypeConstant = isDefiningPrimitiveTypeConstant;
        }

        public IndentedTextWriter Writer { get; }
        public INamespaceSymbol? CurrentNamespace { get; }
        public bool IsDefiningPrimitiveTypeConstant { get; }

        public GenerationContext WithIsDefiningPrimitiveTypeConstant(bool isDefiningPrimitiveTypeConstant)
        {
            return new GenerationContext(Writer, CurrentNamespace, isDefiningPrimitiveTypeConstant);
        }

        public bool IsInCurrentNamespace(INamedTypeSymbol type)
        {
            return SymbolEqualityComparer.Default.Equals(type.ContainingNamespace, CurrentNamespace);
        }

        public void WriteTypeReference(ITypeSymbol type, bool asAttribute = false)
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
            else if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                WriteTypeReference(((INamedTypeSymbol)type).TypeArguments.Single());
                Writer.Write('?');
            }
            else if (type is IArrayTypeSymbol array)
            {
                if (!array.Sizes.IsDefaultOrEmpty || !array.LowerBounds.IsDefaultOrEmpty) throw new NotImplementedException();

                WriteTypeReference(array.ElementType);
                Writer.Write('[');

                for (var i = 1; i < array.Rank; i++)
                    Writer.Write(',');

                Writer.Write(']');
            }
            else if (type is IPointerTypeSymbol pointer)
            {
                WriteTypeReference(pointer.PointedAtType);
                Writer.Write('*');
            }
            else if (type is INamedTypeSymbol named)
            {
                if (string.IsNullOrWhiteSpace(named.Name))
                {
                    Writer.Write("/* ERROR */");
                    return;
                }

                if (type.ContainingType is { })
                {
                    WriteTypeReference(named.ContainingType);
                    Writer.Write('.');
                }
                else if (!IsInCurrentNamespace(named))
                {
                    if (type.ContainingNamespace.IsGlobalNamespace)
                    {
                        // This is unusual and really needs to stand out unambiguously from types that are in the
                        // current namespace.
                        Writer.Write("global::");
                    }
                    else
                    {
                        WriteNamespace(type.ContainingNamespace);
                        Writer.Write('.');
                    }
                }

                WriteIdentifier(asAttribute
                    ? type.Name.RemoveEnd("Attribute", StringComparison.Ordinal)
                    : type.Name);

                if (named.TypeArguments.Any())
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
                WriteIdentifier(type.Name);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public void WriteNamespace(INamespaceSymbol @namespace)
        {
            var parts = new List<string>();

            for (var current = @namespace; current is { IsGlobalNamespace: false }; current = current.ContainingNamespace)
                parts.Add(current.Name);

            for (var i = parts.Count - 1; i >= 0; i--)
            {
                Writer.Write(parts[i]);
                if (i != 0) Writer.Write('.');
            }
        }

        public void WriteLiteral(ITypeSymbol type, object? value)
        {
            if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                throw new NotImplementedException();

            if (type.TypeKind == TypeKind.Enum)
            {
                if (MetadataFacts.GetCombinedEnumMembers(type, value) is { } operation)
                {
                    operation.Write(this);
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
            else switch (value)
            {
                case null:
                    Writer.Write(CanUseNullKeyword(type) ? "null" : "default");
                    break;
                case bool b:
                    WriteLiteral(b);
                    break;
                case int i:
                    WriteLiteral(i);
                    break;
                case uint u:
                    WriteLiteral(u);
                    break;
                case long l:
                    WriteLiteral(l);
                    break;
                case ulong u:
                    WriteLiteral(u);
                    break;
                case byte b:
                    WriteLiteral(b);
                    break;
                case sbyte s:
                    WriteLiteral(s);
                    break;
                case short s:
                    WriteLiteral(s);
                    break;
                case ushort u:
                    WriteLiteral(u);
                    break;
                case string s:
                    WriteLiteral(s);
                    break;
                case char c:
                    WriteLiteral(c);
                    break;
                case double d:
                    WriteLiteral(d);
                    break;
                case float f:
                    WriteLiteral(f);
                    break;
                case decimal d:
                    WriteLiteral(d);
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        private static bool CanUseNullKeyword(ITypeSymbol type)
        {
            return type.IsReferenceType || type is IPointerTypeSymbol || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        }

        public void WriteLiteral(bool value)
        {
            Writer.Write(value ? "true" : "false");
        }

        public void WriteLiteral(int value)
        {
            if (!IsDefiningPrimitiveTypeConstant)
            {
                if (value == int.MaxValue)
                {
                    Writer.Write("int.MaxValue");
                    return;
                }
                if (value == int.MinValue)
                {
                    Writer.Write("int.MinValue");
                    return;
                }
            }

            SyntaxFactory.Literal(value).WriteTo(Writer);
        }

        public void WriteLiteral(uint value)
        {
            if (!IsDefiningPrimitiveTypeConstant)
            {
                if (value == uint.MaxValue)
                {
                    Writer.Write("uint.MaxValue");
                    return;
                }
            }

            SyntaxFactory.Literal(value).WriteTo(Writer);
        }

        public void WriteLiteral(long value)
        {
            if (!IsDefiningPrimitiveTypeConstant)
            {
                if (value == long.MaxValue)
                {
                    Writer.Write("long.MaxValue");
                    return;
                }
                if (value == long.MinValue)
                {
                    Writer.Write("long.MinValue");
                    return;
                }
            }

            SyntaxFactory.Literal(value).WriteTo(Writer);
        }

        public void WriteLiteral(ulong value)
        {
            if (!IsDefiningPrimitiveTypeConstant)
            {
                if (value == ulong.MaxValue)
                {
                    Writer.Write("ulong.MaxValue");
                    return;
                }
                if (value == ulong.MinValue)
                {
                    Writer.Write("ulong.MinValue");
                    return;
                }
            }

            SyntaxFactory.Literal(value).WriteTo(Writer);
        }

        public void WriteLiteral(byte value)
        {
            SyntaxFactory.Literal(value).WriteTo(Writer);
        }

        public void WriteLiteral(sbyte value)
        {
            SyntaxFactory.Literal(value).WriteTo(Writer);
        }

        public void WriteLiteral(short value)
        {
            SyntaxFactory.Literal(value).WriteTo(Writer);
        }

        public void WriteLiteral(ushort value)
        {
            SyntaxFactory.Literal(value).WriteTo(Writer);
        }

        public void WriteLiteral(double value)
        {
            if (double.IsInfinity(value))
            {
                if (double.IsPositiveInfinity(value))
                    Writer.Write(IsDefiningPrimitiveTypeConstant ? "1d / 0d" : "double.PositiveInfinity");
                else if (double.IsNegativeInfinity(value))
                    Writer.Write(IsDefiningPrimitiveTypeConstant ? "-1d / 0d" : "double.NegativeInfinity");
                else
                    throw new NotImplementedException();
                return;
            }

            if (double.IsNaN(value))
            {
                Writer.Write(IsDefiningPrimitiveTypeConstant ? "-0d / 0d" : "double.NaN");
                return;
            }

            if (!IsDefiningPrimitiveTypeConstant)
            {
                if (value == double.MaxValue)
                {
                    Writer.Write("double.MaxValue");
                    return;
                }
                if (value == double.MinValue)
                {
                    Writer.Write("double.MinValue");
                    return;
                }
                if (value == double.Epsilon)
                {
                    Writer.Write("double.Epsilon");
                    return;
                }
            }

            SyntaxFactory.Literal(value).WriteTo(Writer);
        }

        public void WriteLiteral(float value)
        {
            if (float.IsInfinity(value))
            {
                if (float.IsPositiveInfinity(value))
                    Writer.Write(IsDefiningPrimitiveTypeConstant ? "1f / 0f" : "float.PositiveInfinity");
                else if (float.IsNegativeInfinity(value))
                    Writer.Write(IsDefiningPrimitiveTypeConstant ? "-1f / 0f" : "float.NegativeInfinity");
                else
                    throw new NotImplementedException();
                return;
            }

            if (float.IsNaN(value))
            {
                Writer.Write(IsDefiningPrimitiveTypeConstant ? "-0f / 0f" : "float.NaN");
                return;
            }

            if (!IsDefiningPrimitiveTypeConstant)
            {
                if (value == float.MaxValue)
                {
                    Writer.Write("float.MaxValue");
                    return;
                }
                if (value == float.MinValue)
                {
                    Writer.Write("float.MinValue");
                    return;
                }
                if (value == float.Epsilon)
                {
                    Writer.Write("float.Epsilon");
                    return;
                }
            }

            SyntaxFactory.Literal(value).WriteTo(Writer);
        }

        public void WriteLiteral(decimal value)
        {
            if (!IsDefiningPrimitiveTypeConstant)
            {
                if (value == decimal.MaxValue)
                {
                    Writer.Write("decimal.MaxValue");
                    return;
                }
                if (value == decimal.MinValue)
                {
                    Writer.Write("decimal.MinValue");
                    return;
                }
            }

            SyntaxFactory.Literal(value).WriteTo(Writer);
        }

        public void WriteLiteral(char value)
        {
            SyntaxFactory.Literal(value).WriteTo(Writer);
        }

        public void WriteLiteral(string value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));

            SyntaxFactory.Literal(value).WriteTo(Writer);
        }

        public void WriteTypedConstant(TypedConstant constant)
        {
            switch (constant.Kind)
            {
                case TypedConstantKind.Enum:
                case TypedConstantKind.Primitive:
                    WriteLiteral(constant.Type, constant.Value);
                    break;

                case TypedConstantKind.Type:
                    Writer.Write("typeof(");
                    WriteTypeReference((ITypeSymbol)constant.Value!);
                    Writer.Write(')');
                    break;

                default:
                    throw new NotImplementedException(constant.Kind.ToString());
            }
        }

        public void WriteIdentifier(string name)
        {
            if (SyntaxFacts.IsReservedKeyword(SyntaxFacts.GetKeywordKind(name)))
                Writer.Write('@');

            Writer.Write(name);
        }
    }
}
