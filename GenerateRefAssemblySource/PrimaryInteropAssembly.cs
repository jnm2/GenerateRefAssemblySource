using System;
using System.Reflection;

namespace GenerateRefAssemblySource
{
    internal record PrimaryInteropAssembly(AssemblyName AssemblyName, Guid Guid, Version Version);
}
