using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace GenerateRefAssemblySource
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            const string targetFramework = "net35";

            var sourceFolder = args.Single();
            var outputDirectory = Directory.GetCurrentDirectory();

            var generator = new SourceGenerator(GenerationOptions.RefAssembly);

            var dllFilePaths = Directory.GetFiles(sourceFolder, "*.dll");

            var sourceReferences = dllFilePaths
                .Where(path =>
                {
                    using var stream = File.OpenRead(path);
                    using var peReader = new PEReader(stream);

                    return peReader.GetMetadataReader().IsAssembly;
                })
                .Select(path => MetadataReference.CreateFromFile(path))
                .ToImmutableArray();

            var compilation = CSharpCompilation.Create(
                assemblyName: "Dummy compilation",
                syntaxTrees: null,
                sourceReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, metadataImportOptions: MetadataImportOptions.Public));

            if (compilation.GetDiagnostics() is { IsEmpty: false } diagnostics)
                throw new NotImplementedException(string.Join(Environment.NewLine, diagnostics));

            var coreLibrary = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
            if (coreLibrary.Name == "<Missing Core Assembly>")
            {
                Console.WriteLine("No core library (defining System.Object) was provided as a source assembly or lib assembly.");
                return 1;
            }

            var isDefiningTargetFramework = sourceReferences.Contains(compilation.GetMetadataReference(coreLibrary));

            using (var writer = File.CreateText(Path.Join(outputDirectory, "Directory.Build.props")))
                WriteDirectoryBuildProps(writer, isDefiningTargetFramework);

            var projectsByAssemblyName = new Dictionary<string, (Guid Id, string FullPath)>();

            var referencedAssemblies = compilation.Assembly.Modules.Single().ReferencedAssemblySymbols
                .Select(a => (Symbol: a, TypeDeclarationAnalysis: new TypeDeclarationAnalysis(a)))
                .ToList();

            var graph = referencedAssemblies.ToDictionary(
                assembly => assembly.Symbol.Name,
                assembly => (IEnumerable<string>)assembly.TypeDeclarationAnalysis.GetReferencedAssemblyNames());

            var cycleEdges = GraphUtils.GetCycleEdges(graph);

            foreach (var (assembly, typeDeclarationAnalysis) in referencedAssemblies)
            {
                var fileSystem = new ProjectFileSystem(Path.Join(outputDirectory, assembly.Name));
                var projectFileName = assembly.Name + ".csproj";

                var publicSignKeyPath = assembly.Identity.HasPublicKey ? "Public.snk" : null;
                if (publicSignKeyPath is not null)
                    fileSystem.Create(publicSignKeyPath, assembly.Identity.PublicKey);

                using (var writer = fileSystem.CreateText(projectFileName))
                {
                    var (assemblyReferences, projectReferences) = graph[assembly.Name]
                        .Partition(name => cycleEdges.Contains((Dependent: assembly.Name, Dependency: name)));

                    var runtimeMetadataVersion = assembly.GetTypeByMetadataName("System.Object") is not null
                        ? assembly.GetMetadata()?.GetModules().Single().GetMetadataReader().MetadataVersion
                        : null;

                    WriteProjectFile(writer, targetFramework, assemblyReferences, projectReferences, runtimeMetadataVersion, publicSignKeyPath);
                }

                projectsByAssemblyName.Add(assembly.Name, (Guid.NewGuid(), fileSystem.GetPath(projectFileName)));

                generator.Generate(assembly, typeDeclarationAnalysis, fileSystem);
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

            return 0;
        }

        private static void WriteDirectoryBuildProps(TextWriter writer, bool isDefiningTargetFramework)
        {
            writer.Write(
@"<Project>

  <PropertyGroup>
    <ProduceOnlyReferenceAssembly>true</ProduceOnlyReferenceAssembly>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>");

            if (isDefiningTargetFramework)
            {
                writer.Write(@"
    <NoStdLib>true</NoStdLib>");
            }

            writer.Write(@"
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>

</Project>
");
        }

        private static void WriteProjectFile(
            TextWriter writer,
            string targetFramework,
            ImmutableArray<string> assemblyReferences,
            ImmutableArray<string> projectReferences,
            string? runtimeMetadataVersion,
            string? publicSignKeyPath)
        {
            writer.Write(
$@"<Project Sdk=""Microsoft.NET.Sdk"">

  <PropertyGroup>
    <TargetFramework>{targetFramework}</TargetFramework>");

            if (runtimeMetadataVersion is not null)
            {
                writer.Write($@"
    <RuntimeMetadataVersion>{runtimeMetadataVersion}</RuntimeMetadataVersion>");
            }

            if (publicSignKeyPath is not null)
            {
                writer.Write($@"
    <SignAssembly>true</SignAssembly>
    <PublicSign>True</PublicSign>
    <AssemblyOriginatorKeyFile>{publicSignKeyPath}</AssemblyOriginatorKeyFile>");
            }

            writer.Write(@"
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
