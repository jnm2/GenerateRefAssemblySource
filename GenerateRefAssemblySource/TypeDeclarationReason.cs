﻿using System;

namespace GenerateRefAssemblySource
{
    [Flags]
    internal enum TypeDeclarationReason
    {
        ExternallyVisible = 1 << 0,
        ReferencedInConstant = 1 << 1,
        DeclaresUsedAttribute = 1 << 2,
    }
}
