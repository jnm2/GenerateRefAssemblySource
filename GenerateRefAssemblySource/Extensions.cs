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

        // https://github.com/dotnet/runtime/issues/14386
        public static string RemoveEnd(this string instance, string value, StringComparison comparisonType)
        {
            return instance.EndsWith(value, comparisonType)
                ? instance[0..^value.Length]
                : instance;
        }

        public static ImmutableArray<T> EmptyIfDefault<T>(this ImmutableArray<T> instance)
        {
            return instance.IsDefault ? ImmutableArray<T>.Empty : instance;
        }
    }
}
