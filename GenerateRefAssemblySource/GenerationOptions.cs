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

        public static GenerationOptions MinimalPseudocode { get; } = new GenerationOptions(
            GeneratedBodyOptions.MinimalPseudocode,
            generateRequiredBaseConstructorCalls: false);

        public static GenerationOptions RefAssembly { get; } = new GenerationOptions(
            GeneratedBodyOptions.RefAssembly,
            generateRequiredBaseConstructorCalls: true);

        public GenerationOptions(
            GeneratedBodyOptions bodyOptions,
            ImmutableArray<TypeMemberSortKind>? typeMemberOrder = null,
            bool generateRequiredBaseConstructorCalls = false)
        {
            BodyOptions = bodyOptions;
            TypeMemberOrder = typeMemberOrder ?? DefaultTypeMemberOrder;
            GenerateRequiredBaseConstructorCalls = generateRequiredBaseConstructorCalls;
        }

        public GeneratedBodyOptions BodyOptions { get; }
        public ImmutableArray<TypeMemberSortKind> TypeMemberOrder { get; }
        public bool GenerateRequiredBaseConstructorCalls { get; }
    }
}
