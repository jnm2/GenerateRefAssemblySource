using System.IO;

namespace GenerateRefAssemblySource
{
    public interface IProjectFileSystem
    {
        TextWriter CreateText(string relativePath);
    }
}
