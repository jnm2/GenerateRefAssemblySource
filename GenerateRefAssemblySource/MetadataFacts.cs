using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace GenerateRefAssemblySource
{
    internal static class MetadataFacts
    {
        public static (string Name, int Arity) ParseTypeName(string metadataName)
        {
            var backtickIndex = metadataName.IndexOf('`');
            if (backtickIndex == -1) return (metadataName, 0);

            return (
                metadataName.Substring(0, backtickIndex),
                int.Parse(metadataName.AsSpan(backtickIndex + 1), NumberStyles.None, CultureInfo.InvariantCulture));
        }

        public static ImmutableArray<Type> GetContainingTypes(Type type)
        {
            var builder = ImmutableArray.CreateBuilder<Type>();
            var current = type;

            while (true)
            {
                current = current.DeclaringType;
                if (current is null) break;
                builder.Add(current);
            }

            builder.Reverse();
            return builder.ToImmutable();
        }

        public static ImmutableArray<Type> GetNewGenericTypeParameters(Type type)
        {
            return type.GetGenericArguments()
                .Skip(type.DeclaringType?.GetGenericArguments().Length ?? 0)
                .ToImmutableArray();
        }

        public static bool IsVisibleOutsideAssembly(Type type)
        {
            switch (type.Attributes & TypeAttributes.VisibilityMask)
            {
                case TypeAttributes.Public:
                    return true;

                case TypeAttributes.NestedPublic:
                    return IsVisibleOutsideAssembly(type.DeclaringType!);

                case TypeAttributes.NestedFamily:
                case TypeAttributes.NestedFamORAssem:
                    return IsVisibleOutsideAssembly(type.DeclaringType!) && IsInheritable(type.DeclaringType!);

                default:
                    return false;
            }
        }

        public static bool IsInheritable(Type type)
        {
            return type is { IsValueType: false, IsSealed: false }
                && type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Any(IsVisibleToDerivedTypes);
        }

        public static bool IsVisibleToDerivedTypes(MethodBase method)
        {
            switch (method.Attributes & MethodAttributes.MemberAccessMask)
            {
                case MethodAttributes.Public:
                case MethodAttributes.Family:
                case MethodAttributes.FamORAssem:
                    return true;

                default:
                    return false;
            }
        }
    }
}
