using System.IO;

namespace GenerateRefAssemblySource
{
    public interface IProjectFileSystem
    {
        TextWriter Create(string relativePath);
    }
}
