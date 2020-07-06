using System;
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

        public TextWriter CreateText(string relativePath)
        {
            var path = GetPath(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return File.CreateText(path);
        }

        public string GetPath(string relativePath)
        {
            if (Path.IsPathFullyQualified(relativePath))
                throw new ArgumentException("A relative path must be specified.", nameof(relativePath));

            return Path.Join(baseDirectory, relativePath);
        }
    }
}
