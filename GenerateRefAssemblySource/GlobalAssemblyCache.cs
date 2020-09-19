using Microsoft.Win32;
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace GenerateRefAssemblySource
{
    internal sealed class GlobalAssemblyCache
    {
        private static readonly Lazy<GlobalAssemblyCache?> globalAssemblyCache = new(() =>
        {
            if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "InstallPath", defaultValue: null) is not string installPath
                || !Directory.Exists(installPath))
            {
                return null;
            }

            SetDllDirectory(installPath);

            CreateAssemblyCache(out var assemblyCache, dwReserved: 0);

            return new GlobalAssemblyCache(assemblyCache);
        });

        private readonly IAssemblyCache assemblyCache;

        private GlobalAssemblyCache(IAssemblyCache assemblyCache)
        {
            this.assemblyCache = assemblyCache;
        }

        public ImmutableArray<string> GetInstalledAssemblyNames(string? fullNameAsFilter)
        {
            IAssemblyName? filterAssemblyName;

            if (fullNameAsFilter is not null)
                CreateAssemblyNameObject(out filterAssemblyName, fullNameAsFilter, CreateAssemblyNameObjectFlags.ParseDisplayName, pvReserved: IntPtr.Zero);
            else
                filterAssemblyName = null;

            CreateAssemblyEnum(out var assemblyEnum, pUnkReserved: IntPtr.Zero, filterAssemblyName, ASM_CACHE_FLAGS.GAC, pvReserved: IntPtr.Zero);

            var builder = ImmutableArray.CreateBuilder<string>();

            unsafe
            {
                const int bufferSize = 1024;
                var displayNameBuffer = stackalloc char[bufferSize];

                while (true)
                {
                    var result = assemblyEnum.GetNextAssembly(pvReserved: IntPtr.Zero, out var installedAssemblyName, dwFlags: 0);
                    if (result.IsError) throw result.GetException()!;
                    if (result == HRESULT.FALSE) break;

                    var charCountIncludingTerminator = bufferSize;
                    installedAssemblyName.GetDisplayName(displayNameBuffer, ref charCountIncludingTerminator, ASM_DISPLAYF.FULL);
                    builder.Add(new string(displayNameBuffer, 0, charCountIncludingTerminator - 1));
                }
            }

            return builder.ToImmutable();
        }

        public string? Resolve(string fullName)
        {
            var installedName = GetInstalledAssemblyNames(fullName).SingleOrDefault();
            if (installedName is null) return null;

            unsafe
            {
                const int bufferCharCount = 1024;
                var buffer = stackalloc char[bufferCharCount];

                var assemblyInfo = new ASSEMBLY_INFO
                {
                    cbAssemblyInfo = sizeof(ASSEMBLY_INFO),
                    pszCurrentAssemblyPathBuf = buffer,
                    cchBuf = bufferCharCount,
                };

                var result = assemblyCache.QueryAssemblyInfo(QUERYASMINFO_FLAG.VALIDATE, installedName, ref assemblyInfo);

                if (result.IsError)
                {
                    if (result.Code == ERROR.FILE_NOT_FOUND) return null;
                    throw result.GetException()!;
                }

                return new string(buffer);
            }
        }

        public static GlobalAssemblyCache? TryLoad() => globalAssemblyCache.Value;

#pragma warning disable CS0649

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("fusion.dll", PreserveSig = false)]
        private static extern void CreateAssemblyCache(out IAssemblyCache ppAsmCache, uint dwReserved);

        [DllImport("fusion.dll", PreserveSig = false)]
        private static extern void CreateAssemblyEnum(out IAssemblyEnum ppEnum, IntPtr pUnkReserved, IAssemblyName? pName, ASM_CACHE_FLAGS dwFlags, IntPtr pvReserved);

        [Flags]
        private enum ASM_CACHE_FLAGS : uint
        {
            ZAP = 1 << 0,
            GAC = 1 << 1,
            DOWNLOAD = 1 << 2,
            ROOT = 1 << 3,
            ROOT_EX = 1 << 4,
        }

        [DllImport("fusion.dll", PreserveSig = false)]
        private static extern void CreateAssemblyNameObject(out IAssemblyName ppEnum, string szAssemblyName, CreateAssemblyNameObjectFlags dwFlags, IntPtr pvReserved);

        /// <summary>
        /// See <c>CreateAssemblyNameObject</c> definition in <c>src/coreclr/src/binder/fusionassemblyname.cpp</c> at
        /// <see cref="https://github.com/dotnet/runtime"/>.
        /// </summary>
        [Flags]
        private enum CreateAssemblyNameObjectFlags : uint
        {
            ParseDisplayName = 1,
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("e707dcde-d1cd-11d2-bab9-00c04f8eceae")]
        private interface IAssemblyCache
        {
            void Slot1();

            [PreserveSig]
            HRESULT QueryAssemblyInfo(QUERYASMINFO_FLAG dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string pszAssemblyName, ref ASSEMBLY_INFO pAsmInfo);
        }


        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("21b8916c-f28e-11d2-a473-00c04f8ef448")]
        private interface IAssemblyEnum
        {
            [PreserveSig]
            HRESULT GetNextAssembly(IntPtr pvReserved, out IAssemblyName ppName, uint dwFlags);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("cd193bc0-b4bc-11d2-9833-00c04fc31d2e")]
        private interface IAssemblyName
        {
            void Slot1();
            void Slot2();
            void Slot3();

            unsafe void GetDisplayName(char* szDisplayName, ref int pccDisplayName, ASM_DISPLAYF dwDisplayFlags);
        }

        [Flags]
        private enum ASM_DISPLAYF : uint
        {
            VERSION = 1 << 0,
            CULTURE = 1 << 1,
            PUBLIC_KEY_TOKEN = 1 << 2,
            PUBLIC_KEY = 1 << 3,
            CUSTOM = 1 << 4,
            PROCESSORARCHITECTURE = 1 << 5,
            LANGUAGEID = 1 << 6,
            RETARGET = 1 << 7,
            CONFIG_MASK = 1 << 8,
            MVID = 1 << 9,
            FULL = VERSION | CULTURE | PUBLIC_KEY_TOKEN | RETARGET | PROCESSORARCHITECTURE,
        }

        [Flags]
        private enum QUERYASMINFO_FLAG : uint
        {
            VALIDATE = 1 << 0,
            GETSIZE = 1 << 1,
        }

        private struct ASSEMBLY_INFO
        {
            public int cbAssemblyInfo;
            public uint dwAssemblyFlags;
            public ulong uliAssemblySizeInKB;
            public unsafe char* pszCurrentAssemblyPathBuf;
            public uint cchBuf;
        }

        private readonly struct HRESULT : IEquatable<HRESULT>
        {
            public static HRESULT FALSE { get; } = new HRESULT(1);

            private readonly uint value;

            public HRESULT(uint value) => this.value = value;

            public bool IsError => (value & 0x80000000) != 0;

            public ERROR Code => (ERROR)value;

            public Exception? GetException() => Marshal.GetExceptionForHR((int)value);

            public override bool Equals(object? obj) => obj is HRESULT hresult && Equals(hresult);

            public bool Equals(HRESULT other) => value == other.value;

            public override int GetHashCode() => value.GetHashCode();

            public static bool operator ==(HRESULT left, HRESULT right) => left.value == right.value;

            public static bool operator !=(HRESULT left, HRESULT right) => left.value != right.value;
        }

        private enum ERROR : ushort
        {
            FILE_NOT_FOUND = 2,
        }
    }
}
