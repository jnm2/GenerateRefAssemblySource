using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;
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

        public string? Resolve(AssemblyName assemblyName)
        {
            var arch = assemblyName.ProcessorArchitecture;
            if (arch == ProcessorArchitecture.None) arch = ProcessorArchitecture.MSIL;
            var name = assemblyName.FullName + ", ProcessorArchitecture=" + arch;

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

                var result = assemblyCache.QueryAssemblyInfo(QUERYASMINFO_FLAG.VALIDATE, name, ref assemblyInfo);

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

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("e707dcde-d1cd-11d2-bab9-00c04f8eceae")]
        private interface IAssemblyCache
        {
            void UninstallAssembly(uint dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string pszAssemblyName, IntPtr pRefData, out uint pulDisposition);

            [PreserveSig]
            HRESULT QueryAssemblyInfo(QUERYASMINFO_FLAG dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string pszAssemblyName, ref ASSEMBLY_INFO pAsmInfo);

            void CreateAssemblyCacheItem(uint dwFlags, IntPtr pvReserved, out IntPtr ppAsmItem, [MarshalAs(UnmanagedType.LPWStr)] string? pszAssemblyName);

            IntPtr CreateAssemblyScavenger();

            void InstallAssembly(uint dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string pszManifestFilePath, IntPtr pRefData);
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

        private readonly struct HRESULT
        {
            private readonly uint value;

            public bool IsError => (value & 0x80000000) != 0;

            public ERROR Code => (ERROR)value;

            public Exception? GetException() => Marshal.GetExceptionForHR((int)value);
        }

        private enum ERROR : ushort
        {
            FILE_NOT_FOUND = 2,
        }
    }
}
