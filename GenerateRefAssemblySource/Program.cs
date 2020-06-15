using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
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

            var generator = new SourceGenerator(new GenerationOptions(GeneratedBodyOptions.RefAssembly));

            var dllFilePaths = Directory.GetFiles(sourceFolder, "*.dll");

            var compilation = CSharpCompilation.Create(
                assemblyName: string.Empty,
                syntaxTrees: null,
                dllFilePaths.Select(path => MetadataReference.CreateFromFile(path)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, metadataImportOptions: MetadataImportOptions.Public));

            var projectsByAssemblyName = new Dictionary<string, (Guid Id, string FullPath)>();

            foreach (var reference in compilation.References)
            {
                var assembly = (IAssemblySymbol?)compilation.GetAssemblyOrModuleSymbol(reference);
                if (assembly is null) continue;

                var projectFolder = Path.Join(outputDirectory, assembly.Name);
                var projectFilePath = Path.Join(projectFolder, assembly.Name + ".csproj");

                File.WriteAllText(
                    projectFilePath,
@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>net35</TargetFramework>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>
    <NoStdLib>true</NoStdLib>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>

</Project>
");
                projectsByAssemblyName.Add(assembly.Name, (Guid.NewGuid(), projectFilePath));

                generator.Generate(assembly, new ProjectFileSystem(projectFolder));
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
    }
}
