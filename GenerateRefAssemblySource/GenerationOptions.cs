using System.Collections.Immutable;

namespace GenerateRefAssemblySource
{
    public sealed class GenerationOptions
    {
        private static readonly ImmutableArray<TypeMemberSortKind> DefaultTypeMemberOrder = ImmutableArray.Create(
            TypeMemberSortKind.Constant,
            TypeMemberSortKind.Field,
            TypeMemberSortKind.Constructor,
            TypeMemberSortKind.Property,
            TypeMemberSortKind.Indexer,
            TypeMemberSortKind.Event,
            TypeMemberSortKind.Method,
            TypeMemberSortKind.Operator,
            TypeMemberSortKind.Conversion);

        public GenerationOptions(GeneratedBodyOptions bodyOptions, ImmutableArray<TypeMemberSortKind>? typeMemberOrder = null)
        {
            BodyOptions = bodyOptions;
            TypeMemberOrder = typeMemberOrder ?? DefaultTypeMemberOrder;
        }

        public GeneratedBodyOptions BodyOptions { get; }

        public ImmutableArray<TypeMemberSortKind> TypeMemberOrder { get; }
    }
}
