using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace GenerateRefAssemblySource
{
    internal sealed class NamespaceOrTypeFullNameComparer : IComparer<INamespaceOrTypeSymbol?>
    {
        /// <summary>
        /// This class has no instance state.
        /// </summary>
        public static NamespaceOrTypeFullNameComparer Instance { get; } = new NamespaceOrTypeFullNameComparer();
        private NamespaceOrTypeFullNameComparer() { }

        public int Compare(INamespaceOrTypeSymbol? x, INamespaceOrTypeSymbol? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            var xSegments = GetSegments(x);
            var ySegments = GetSegments(y);

            for (var i = 0; ; i++)
            {
                if (i == xSegments.Length)
                    return i == ySegments.Length ? 0 : -1;
                if (i == ySegments.Length)
                    return 1;

                var comparison = xSegments[i].CompareTo(ySegments[i]);
                if (comparison != 0) return comparison;
            }
        }

        private static ImmutableArray<(string Name, int Arity)> GetSegments(INamespaceOrTypeSymbol symbol)
        {
            var builder = ImmutableArray.CreateBuilder<(string Name, int Arity)>();

            while (symbol is not INamespaceSymbol { IsGlobalNamespace: true })
            {
                builder.Add((symbol.Name, (symbol as INamedTypeSymbol)?.Arity ?? 0));

                symbol = (INamespaceOrTypeSymbol)symbol.ContainingSymbol;
            }

            builder.Reverse();
            return builder.ToImmutable();
        }
    }
}
