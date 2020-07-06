using System.Collections.Immutable;
using System.IO;

namespace GenerateRefAssemblySource
{
    public interface IProjectFileSystem
    {
        string GetPath(string relativePath);
        void Create(string relativePath, ImmutableArray<byte> contents);
        TextWriter CreateText(string relativePath);
    }
}
