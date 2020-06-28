using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace GenerateRefAssemblySource
{
    internal static class Extensions
    {
        public static (ImmutableArray<T> True, ImmutableArray<T> False) Partition<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            var trueBuilder = ImmutableArray.CreateBuilder<T>();
            var falseBuilder = ImmutableArray.CreateBuilder<T>();

            foreach (var value in source)
            {
                (predicate(value) ? trueBuilder : falseBuilder).Add(value);
            }

            return (trueBuilder.ToImmutable(), falseBuilder.ToImmutable());
        }
    }
}
