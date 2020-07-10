using Microsoft.CodeAnalysis;
using System;
using System.Linq.Expressions;
using System.Reflection;

namespace GenerateRefAssemblySource
{
    /// <summary>
    /// Voids warranty. Use only if this makes you happy.
    /// </summary>
    internal static class InternalAccessUtils
    {
        private const BindingFlags NonPublicInstanceDeclaredOnly = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        static InternalAccessUtils()
        {
            var typeSymbolParameter = Expression.Parameter(typeof(ITypeSymbol));
            var kindParameter = Expression.Parameter(typeof(TypedConstantKind));
            var valueParameter = Expression.Parameter(typeof(object));

            var typeSymbolInternalType = Assembly.Load("Microsoft.CodeAnalysis")
                .GetType("Microsoft.CodeAnalysis.Symbols.ITypeSymbolInternal", throwOnError: true)!;

            var publicTypeSymbolType = Assembly.Load("Microsoft.CodeAnalysis.CSharp")
                .GetType("Microsoft.CodeAnalysis.CSharp.Symbols.PublicModel.TypeSymbol", throwOnError: true)!;

            createTypeConstant = Expression.Lambda<Func<ITypeSymbol, TypedConstantKind, object?, TypedConstant>>(
                Expression.New(
                    typeof(TypedConstant).GetConstructor(NonPublicInstanceDeclaredOnly, null, new[] { typeSymbolInternalType, typeof(TypedConstantKind), typeof(object) }, null),
                    Expression.Coalesce(
                        Expression.TypeAs(typeSymbolParameter, typeSymbolInternalType),
                        Expression.MakeMemberAccess(
                            Expression.Convert(typeSymbolParameter, publicTypeSymbolType),
                            publicTypeSymbolType.GetProperty("UnderlyingTypeSymbol", NonPublicInstanceDeclaredOnly))),
                    kindParameter,
                    valueParameter),
                typeSymbolParameter,
                kindParameter,
                valueParameter).Compile();
        }

        public static Func<IMethodSymbol, MethodImplAttributes> GetImplementationAttributes { get; } =
            CompileInternalMethodSymbolAccessor<MethodImplAttributes>("ImplementationAttributes");

        private static readonly Func<ITypeSymbol, TypedConstantKind, object?, TypedConstant> createTypeConstant;

        public static TypedConstant CreateTypedConstant(ITypeSymbol typeSymbol, TypedConstantKind kind, object? value) => createTypeConstant(typeSymbol, kind, value);

        private static Func<IMethodSymbol, T> CompileInternalMethodSymbolAccessor<T>(string propertyName)
        {
            var parameter = Expression.Parameter(typeof(IMethodSymbol));

            var publicMethodSymbolType = Assembly.Load("Microsoft.CodeAnalysis.CSharp")
                .GetType("Microsoft.CodeAnalysis.CSharp.Symbols.PublicModel.MethodSymbol", throwOnError: true)!;

            var underlyingMethodSymbolProperty = publicMethodSymbolType
                .GetProperty("UnderlyingMethodSymbol", NonPublicInstanceDeclaredOnly)!;

            return Expression.Lambda<Func<IMethodSymbol, T>>(
                Expression.MakeMemberAccess(
                    Expression.MakeMemberAccess(
                        Expression.Convert(parameter, publicMethodSymbolType),
                        underlyingMethodSymbolProperty),
                    underlyingMethodSymbolProperty
                        .PropertyType
                        .GetProperty(propertyName, NonPublicInstanceDeclaredOnly)),
                parameter).Compile();
        }
    }
}
