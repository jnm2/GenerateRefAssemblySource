using Microsoft.Win32;
using System;

namespace GenerateRefAssemblySource
{
    internal static class ComUtils
    {
        public static string? GetPrimaryInteropAssemblyName(Guid guid, int majorVersion, int minorVersion)
        {
            return (string?)Registry.GetValue($@"HKEY_CLASSES_ROOT\TypeLib\{guid:B}\{majorVersion}.{minorVersion}", "PrimaryInteropAssemblyName", defaultValue: null);
        }
    }
}
