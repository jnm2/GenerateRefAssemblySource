using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;

namespace GenerateRefAssemblySource
{
    internal sealed class PrimaryInteropAssemblyCache
    {
        private readonly ImmutableDictionary<string, ImmutableArray<PrimaryInteropAssembly>> cache;

        public PrimaryInteropAssemblyCache()
        {
            var cacheBuilder = new Dictionary<string, ImmutableArray<PrimaryInteropAssembly>.Builder>(StringComparer.OrdinalIgnoreCase);

            using var typeLibKey = Registry.ClassesRoot.OpenSubKey("TypeLib");

            foreach (var idKeyName in typeLibKey.GetSubKeyNames())
            {
                if (!Guid.TryParseExact(idKeyName, "B", out var guid)) continue;

                using var idKey = typeLibKey.OpenSubKey(idKeyName);

                foreach (var versionKeyName in idKey.GetSubKeyNames())
                {
                    if (!Version.TryParse(versionKeyName, out var version)) continue;

                    using var versionKey = idKey.OpenSubKey(versionKeyName);

                    if (versionKey.GetValue("PrimaryInteropAssemblyName") is not string primaryInteropAssemblyName) continue;

                    AssemblyName parsedAssemblyName;
                    try
                    {
                        parsedAssemblyName = new AssemblyName(primaryInteropAssemblyName);
                    }
                    catch (FileLoadException) // Invalid format
                    {
                        continue;
                    }

                    if (parsedAssemblyName.Name is null) continue;

                    if (!cacheBuilder.TryGetValue(parsedAssemblyName.Name, out var builder))
                    {
                        builder = ImmutableArray.CreateBuilder<PrimaryInteropAssembly>();
                        cacheBuilder.Add(parsedAssemblyName.Name, builder);
                    }

                    builder.Add(new PrimaryInteropAssembly(parsedAssemblyName, guid, version));
                }
            }

            cache = cacheBuilder.ToImmutableDictionary(entry => entry.Key, entry => entry.Value.ToImmutable());
        }

        public PrimaryInteropAssembly? GetClosestVersionGreaterThanOrEqualTo(string requestedAssemblyName, Version requestedVersion)
        {
            return cache.TryGetValue(requestedAssemblyName, out var matches)
                ? matches
                    .Where(m => m.AssemblyName.Version >= requestedVersion)
                    .OrderBy(m => m.AssemblyName.Version)
                    .FirstOrDefault()
                : null;
        }
    }
}
