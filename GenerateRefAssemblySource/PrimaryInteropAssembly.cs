using Microsoft.CodeAnalysis;
using System;

namespace GenerateRefAssemblySource
{
    internal record PrimaryInteropAssembly(AssemblyIdentity AssemblyName, Guid Guid, Version Version);
}
