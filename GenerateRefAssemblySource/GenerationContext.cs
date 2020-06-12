using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.CodeDom.Compiler;
using System.Linq;

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
                        Writer.Write(type.ContainingNamespace.ToDisplayString());
                        Writer.Write('.');
                    }
                }

                WriteIdentifier(type.Name);

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
                WriteIdentifier(type.Name);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public void WriteTargetTypedLiteral(ITypeSymbol type, object? value)
        {
            if (type.TypeKind == TypeKind.Enum)
            {
                var members = MetadataFacts.GetCombinedEnumMembers(type, value);
                if (members.Any())
                {
                    for (var i = 0; i < members.Length; i++)
                    {
                        if (i != 0) Writer.Write(" | ");
                        WriteTypeReference(type);
                        Writer.Write('.');
                        WriteIdentifier(members[i].Name);
                    }
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
                case double d:
                    WriteTargetTypedLiteral(d);
                    break;
                case string s:
                    WriteTargetTypedLiteral(s);
                    break;
                default:
                    Writer.Write("/* TODO */");
                    break;
            }
        }

        private static bool CanUseNullKeyword(ITypeSymbol type)
        {
            return type.IsReferenceType || type is IPointerTypeSymbol || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        }

        public void WriteTargetTypedLiteral(double value)
        {
            if (double.IsInfinity(value))
            {
                if (double.IsPositiveInfinity(value))
                    Writer.Write("double.PositiveInfinity");
                else if (double.IsNegativeInfinity(value))
                    Writer.Write("double.NegativeInfinity");
                else
                    throw new NotImplementedException();
            }
            else if (double.IsNaN(value))
            {
                Writer.Write("double.NaN");
            }
            else if (value == double.MaxValue)
            {
                Writer.Write("double.MaxValue");
            }
            else if (value == double.MinValue)
            {
                Writer.Write("double.MinValue");
            }
            else if (value == double.Epsilon)
            {
                Writer.Write("double.Epsilon");
            }
            else
            {
                SyntaxFactory.Literal(value).WriteTo(Writer);
            }
        }

        public void WriteTargetTypedLiteral(string value)
        {
            if (value is null) throw new ArgumentNullException(nameof(value));

            SyntaxFactory.Literal(value).WriteTo(Writer);
        }

        public void WriteIdentifier(string name)
        {
            if (!SyntaxFacts.IsValidIdentifier(name)) throw new NotImplementedException();

            if (SyntaxFacts.IsReservedKeyword(SyntaxFacts.GetKeywordKind(name)))
                Writer.Write('@');

            Writer.Write(name);
        }
    }
}
