using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace GenerateRefAssemblySource
{
    internal static class GraphUtils
    {
        public static ImmutableHashSet<(T Dependent, T Dependency)> GetCycleEdges<T>(IReadOnlyDictionary<T, IEnumerable<T>> dependenciesByDependent)
            where T : notnull
        {
            var builder = ImmutableHashSet.CreateBuilder<(T Dependent, T Dependency)>();

            foreach (var item in dependenciesByDependent.Keys)
            {
                Visit(ImmutableStack.Create(item));
            }

            return builder.ToImmutable();

            void Visit(ImmutableStack<T> stack)
            {
                if (dependenciesByDependent.TryGetValue(stack.Peek(), out var dependencies))
                {
                    foreach (var dependency in dependencies)
                    {
                        var cycleLength = 0;
                        var cycleFound = false;

                        foreach (var item in stack)
                        {
                            cycleLength++;

                            if (EqualityComparer<T>.Default.Equals(item, dependency))
                            {
                                cycleFound = true;
                                break;
                            }
                        }

                        var nextStack = stack.Push(dependency);

                        if (cycleFound)
                        {
                            var cycleWithRepeat = nextStack.Take(cycleLength + 1);
                            builder.UnionWith(cycleWithRepeat.Skip(1).Zip(cycleWithRepeat));
                        }
                        else
                        {
                            Visit(nextStack);
                        }
                    }
                }
            }
        }
    }
}
