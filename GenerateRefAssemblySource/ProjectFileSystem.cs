using System;
using System.Collections.Immutable;
using System.IO;

namespace GenerateRefAssemblySource
{
    public sealed class ProjectFileSystem : IProjectFileSystem
    {
        private readonly string baseDirectory;

        public ProjectFileSystem(string baseDirectory)
        {
            if (!Path.IsPathFullyQualified(baseDirectory))
                throw new ArgumentException("The base directory path must be fully qualified.", nameof(baseDirectory));

            this.baseDirectory = baseDirectory;
        }

        public string GetPath(string relativePath)
        {
            if (Path.IsPathFullyQualified(relativePath))
                throw new ArgumentException("A relative path must be specified.", nameof(relativePath));

            return Path.Join(baseDirectory, relativePath);
        }

        public void Create(string relativePath, ImmutableArray<byte> contents)
        {
            using var stream = Create(relativePath);
            stream.Write(contents.AsSpan());
        }

        public TextWriter CreateText(string relativePath)
        {
            return new StreamWriter(Create(relativePath));
        }

        private Stream Create(string relativePath)
        {
            var path = GetPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return File.Create(path);
        }
    }
}
