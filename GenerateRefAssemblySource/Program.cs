﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;

namespace GenerateRefAssemblySource
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var sourceFolder = args.Single();
            var outputDirectory = Directory.GetCurrentDirectory();

            var generator = new SourceGenerator(GenerationOptions.RefAssembly);

            var dllFilePaths = Directory.GetFiles(sourceFolder, "*.dll");

            var compilation = CSharpCompilation.Create(
                assemblyName: string.Empty,
                syntaxTrees: null,
                dllFilePaths.Select(path => MetadataReference.CreateFromFile(path)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, metadataImportOptions: MetadataImportOptions.Public));

            var projectsByAssemblyName = new Dictionary<string, (Guid Id, string FullPath)>();

            File.WriteAllText(
                Path.Join(outputDirectory, "Directory.Build.props"),
@"<Project>

  <PropertyGroup>
    <ProduceOnlyReferenceAssembly>true</ProduceOnlyReferenceAssembly>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
    <NoStdLib>true</NoStdLib>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>

</Project>
");
            var referencedAssemblies = compilation.Assembly.Modules.Single().ReferencedAssemblySymbols;

            var graph = referencedAssemblies.ToDictionary(
                assembly => assembly.Name,
                assembly => assembly.Modules
                    .SelectMany(m => m.ReferencedAssemblies, (_, identity) => identity.Name)
                    .Where(name => name != assembly.Name));

            var cycleEdges = GraphUtils.GetCycleEdges(graph);

            foreach (var assembly in referencedAssemblies)
            {
                var fileSystem = new ProjectFileSystem(Path.Join(outputDirectory, assembly.Name));
                var projectFileName = assembly.Name + ".csproj";

                using (var writer = fileSystem.Create(projectFileName))
                {
                    var (assemblyReferences, projectReferences) = graph[assembly.Name]
                        .Partition(name => cycleEdges.Contains((Dependent: assembly.Name, Dependency: name)));

                    WriteProjectFile(writer, assemblyReferences, projectReferences);
                }

                projectsByAssemblyName.Add(assembly.Name, (Guid.NewGuid(), fileSystem.GetPath(projectFileName)));

                generator.Generate(assembly, fileSystem);
            }

            using var slnWriter = new SlnWriter(File.CreateText(Path.Join(outputDirectory, Path.GetFileName(outputDirectory) + ".sln")));

            slnWriter.WriteHeader(
                visualStudioVersion: new Version("16.0.28701.123"),
                minimumVisualStudioVersion: new Version("10.0.40219.1"));

            var sdkCsprojProjectType = new Guid("9A19103F-16F7-4668-BE54-9A1E7A4F7556");

            foreach (var (name, (id, fullPath)) in projectsByAssemblyName)
            {
                slnWriter.WriteProjectStart(sdkCsprojProjectType, name, Path.GetRelativePath(outputDirectory, fullPath), id);
                slnWriter.WriteProjectEnd();
            }
        }

        private static void WriteProjectFile(
            TextWriter writer,
            ImmutableArray<string> assemblyReferences,
            ImmutableArray<string> projectReferences)
        {
            writer.Write(
@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net35</TargetFramework>
  </PropertyGroup>");

            if (assemblyReferences.Any())
            {
                writer.Write(@"

  <ItemGroup>");

                foreach (var reference in assemblyReferences)
                {
                    writer.Write($@"
    <Reference Include=""{reference}"" />");
                }

                writer.Write(@"
  </ItemGroup>");
            }

            if (projectReferences.Any())
            {
                writer.Write(@"

  <ItemGroup>");

                foreach (var reference in projectReferences)
                {
                    writer.Write($@"
    <ProjectReference Include=""..\{reference}\{reference}.csproj"" />");
                }

                writer.Write(@"
  </ItemGroup>");
            }

            writer.Write(@"

</Project>
");
        }
    }
}
