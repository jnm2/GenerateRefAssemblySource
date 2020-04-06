using System.IO;

namespace GenerateRefAssemblySource
{
    internal interface IProjectFileSystem
    {
        TextWriter Create(string relativePath);
    }
}
