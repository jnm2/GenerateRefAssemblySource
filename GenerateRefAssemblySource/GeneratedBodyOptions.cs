﻿namespace GenerateRefAssemblySource
{
    public sealed class GeneratedBodyOptions
    {
        public static GeneratedBodyOptions MinimalPseudocode { get; } = new GeneratedBodyOptions(
            requiredBodyWithVoidReturn: GeneratedBodyType.None,
            requiredBodyWithNonVoidReturn: GeneratedBodyType.None,
            useAutoProperties: true,
            useFieldLikeEvents: true);

        public static GeneratedBodyOptions RefAssembly { get; } = new GeneratedBodyOptions(
            requiredBodyWithVoidReturn: GeneratedBodyType.ThrowNull,
            requiredBodyWithNonVoidReturn: GeneratedBodyType.ThrowNull,
            useAutoProperties: false,
            useFieldLikeEvents: false);

        public GeneratedBodyOptions(
            GeneratedBodyType requiredBodyWithVoidReturn,
            GeneratedBodyType requiredBodyWithNonVoidReturn,
            bool useAutoProperties,
            bool useFieldLikeEvents,
            bool useExpressionBodiedPropertiesWhenThrowingNull = true)
        {
            RequiredBodyWithVoidReturn = requiredBodyWithVoidReturn;
            RequiredBodyWithNonVoidReturn = requiredBodyWithNonVoidReturn;
            UseAutoProperties = useAutoProperties;
            UseFieldLikeEvents = useFieldLikeEvents;
            UseExpressionBodiedPropertiesWhenThrowingNull = useExpressionBodiedPropertiesWhenThrowingNull;
        }

        public GeneratedBodyType RequiredBodyWithVoidReturn { get; }
        public GeneratedBodyType RequiredBodyWithNonVoidReturn { get; }
        public bool UseAutoProperties { get; }
        public bool UseFieldLikeEvents { get; }
        public bool UseExpressionBodiedPropertiesWhenThrowingNull { get; }
    }
}
