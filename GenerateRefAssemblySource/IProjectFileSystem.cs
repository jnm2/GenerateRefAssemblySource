using System.Collections.Immutable;
using System.IO;

namespace GenerateRefAssemblySource
{
    public interface IProjectFileSystem
    {
        string GetPath(string relativePath);
        void Create(string relativePath, ImmutableArray<byte> contents);
        TextWriter CreateText(string relativePath);

        sealed void WriteAllLines(string relativePath, params string[] lines)
        {
            using var writer = CreateText(relativePath);

            foreach (var line in lines)
                writer.WriteLine(line);
        }
    }
}
