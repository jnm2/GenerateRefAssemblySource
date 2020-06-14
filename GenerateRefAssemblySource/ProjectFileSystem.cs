﻿using System;
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

        public TextWriter Create(string relativePath)
        {
            var path = Path.Join(baseDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return File.CreateText(path);
        }
    }
}
