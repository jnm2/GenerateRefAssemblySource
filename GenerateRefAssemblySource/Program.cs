using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

[module: DefaultCharSet(CharSet.Unicode)]

namespace GenerateRefAssemblySource
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var rootCommand = new RootCommand
            {
                new Argument<string>("source", "The directory containing the .dll files to generate API source for."),
                new Option<string>(new[] { "--output", "-o" }, getDefaultValue: () => ".", "The root directory for the output."),
                new Option<string>(new[] { "--lib", "-l" }) { Argument = { Arity = ArgumentArity.ZeroOrMore } },
            };

            rootCommand.Handler = CommandHandler.Create<string, string, string[]>(Run);

            return rootCommand.Invoke(args);
        }

        public static int Run(string source, string output, string[]? lib)
        {
            lib ??= Array.Empty<string>();

            const string targetFramework = "net35";

            var generator = new SourceGenerator(GenerationOptions.RefAssembly);

            var sourceReferences = new List<MetadataReference>();
            var sourceAssemblyNames = new HashSet<string>();
            var requiredAssemblyNames = new HashSet<string>();

            foreach (var sourceFile in Directory.GetFiles(source, "*.dll"))
            {
                using var stream = File.OpenRead(sourceFile);
                using var peReader = new PEReader(stream);

                var reader = peReader.GetMetadataReader();
                if (!reader.IsAssembly) continue;

                sourceReferences.Add(MetadataReference.CreateFromFile(sourceFile));

                sourceAssemblyNames.Add(reader.GetString(reader.GetAssemblyDefinition().Name));

                requiredAssemblyNames.UnionWith(reader.AssemblyReferences.Select(handle =>
                    reader.GetString(reader.GetAssemblyReference(handle).Name)));
            }

            requiredAssemblyNames.ExceptWith(sourceAssemblyNames);

            var libReferences = new List<MetadataReference>();

            foreach (var assemblyName in requiredAssemblyNames)
            {
                foreach (var libFolder in lib)
                {
                    var libFilePath = Path.Join(libFolder, assemblyName + ".dll");
                    if (File.Exists(libFilePath))
                    {
                        libReferences.Add(MetadataReference.CreateFromFile(libFilePath));
                        break;
                    }
                }
            }

            var compilation = CSharpCompilation.Create(
                assemblyName: "Dummy compilation",
                syntaxTrees: null,
                sourceReferences.Concat(libReferences),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, metadataImportOptions: MetadataImportOptions.Public));

            if (compilation.GetDiagnostics() is { IsEmpty: false } diagnostics)
                throw new NotImplementedException(string.Join(Environment.NewLine, diagnostics));

            var sourceAssemblies = sourceReferences
                .Select(compilation.GetAssemblyOrModuleSymbol)
                .OfType<IAssemblySymbol>()
                .Select(a => (Symbol: a, TypeDeclarationAnalysis: new TypeDeclarationAnalysis(a)))
                .ToList();

            var missingAssemblies = sourceAssemblies
                .SelectMany(a => a.TypeDeclarationAnalysis.GetReferencedAssemblies(), (from, to) => (From: from, To: to))
                .GroupBy(dependency => dependency.To, dependency => dependency.From)
                .Where(dependency => compilation.GetMetadataReference(dependency.Key) is null)
                .ToList();

            var comReferencesByDependentAssemblyName = new Dictionary<string, List<PrimaryInteropAssembly>>(StringComparer.OrdinalIgnoreCase);
            var primaryInteropAssemblyReferences = new List<MetadataReference>();

            if (missingAssemblies.Any())
            {
                var cache = new PrimaryInteropAssemblyCache();

                for (var i = missingAssemblies.Count - 1; i >= 0; i--)
                {
                    var identity = missingAssemblies[i].Key.Identity;
                    if (cache.GetClosestVersionGreaterThanOrEqualTo(identity.Name, identity.Version) is { } primaryInteropAssembly)
                    {
                        var gac = GlobalAssemblyCache.TryLoad();
                        if (gac is null) break;

                        var piaPath = gac.Resolve(primaryInteropAssembly.AssemblyName.FullName);
                        if (piaPath is not null)
                        {
                            primaryInteropAssemblyReferences.Add(MetadataReference.CreateFromFile(piaPath));

                            var dependentAssemblies = missingAssemblies[i];

                            Console.Write("Using COM primary interop assembly ");
                            Console.Write(piaPath);
                            Console.Error.Write(" (required by: ");
                            Console.Error.Write(string.Join(", ", dependentAssemblies
                                .Select(a => a.Symbol.Name)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)));
                            Console.Error.WriteLine(")");

                            foreach (var (dependentAssembly, _) in dependentAssemblies)
                            {
                                if (!comReferencesByDependentAssemblyName.TryGetValue(dependentAssembly.Name, out var list))
                                    comReferencesByDependentAssemblyName.Add(dependentAssembly.Name, list = new());
                                list.Add(primaryInteropAssembly);
                            }

                            missingAssemblies.RemoveAt(i);
                        }
                    }
                }

                if (missingAssemblies.Any())
                {
                    Console.Error.WriteLine("These referenced assemblies could not be found in the specified source or lib folders:");

                    foreach (var dependentAssembliesByDependency in missingAssemblies.OrderBy(a => a.Key.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        Console.Error.Write(dependentAssembliesByDependency.Key);
                        Console.Error.Write(" (required by: ");
                        Console.Error.Write(string.Join(", ", dependentAssembliesByDependency
                            .Select(a => a.Symbol.Name)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)));
                        Console.Error.WriteLine(")");
                    }

                    return 1;
                }
            }

            if (primaryInteropAssemblyReferences.Any())
            {
                compilation = compilation.AddReferences(primaryInteropAssemblyReferences);

                diagnostics = compilation.GetDiagnostics();
                if (diagnostics is { IsEmpty: false })
                    throw new NotImplementedException(string.Join(Environment.NewLine, diagnostics));

                sourceAssemblies = sourceReferences
                    .Select(compilation.GetAssemblyOrModuleSymbol)
                    .OfType<IAssemblySymbol>()
                    .Select(a => (Symbol: a, TypeDeclarationAnalysis: new TypeDeclarationAnalysis(a)))
                    .ToList();
            }

            var coreLibrary = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
            var isDefiningTargetFramework = sourceReferences.Contains(compilation.GetMetadataReference(coreLibrary)!);

            Directory.CreateDirectory(output);
            using (var writer = File.CreateText(Path.Join(output, "Directory.Build.props")))
                WriteDirectoryBuildProps(writer, isDefiningTargetFramework);

            var projectsByAssemblyName = new Dictionary<string, (Guid Id, string FullPath)>();

            var graph = sourceAssemblies.ToDictionary(
                assembly => assembly.Symbol.Name,
                assembly => assembly.TypeDeclarationAnalysis.GetReferencedAssemblies().Select(a => a.Name));

            var cycleEdges = GraphUtils.GetCycleEdges(graph);

            foreach (var (assembly, typeDeclarationAnalysis) in sourceAssemblies)
            {
                var fileSystem = new ProjectFileSystem(Path.Join(output, assembly.Name));
                var projectFileName = assembly.Name + ".csproj";

                var publicSignKeyPath = assembly.Identity.HasPublicKey ? "Public.snk" : null;
                if (publicSignKeyPath is not null)
                    fileSystem.Create(publicSignKeyPath, assembly.Identity.PublicKey);

                using (var writer = fileSystem.CreateText(projectFileName))
                {
                    var (assemblyReferences, projectReferences) = graph[assembly.Name]
                        .Partition(name =>
                            !sourceAssemblyNames.Contains(name)
                            || cycleEdges.Contains((Dependent: assembly.Name, Dependency: name)));

                    var runtimeMetadataVersion = assembly.GetTypeByMetadataName("System.Object") is not null
                        ? assembly.GetMetadata()?.GetModules().Single().GetMetadataReader().MetadataVersion
                        : null;

                    var comReferences = comReferencesByDependentAssemblyName.GetValueOrDefault(assembly.Name)?.ToImmutableArray() ?? ImmutableArray<PrimaryInteropAssembly>.Empty;

                    WriteProjectFile(
                        writer,
                        targetFramework,
                        assemblyReferences.Except(comReferences.Select(r => r.AssemblyName.Name!), StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
                        comReferences,
                        projectReferences,
                        runtimeMetadataVersion,
                        publicSignKeyPath);
                }

                projectsByAssemblyName.Add(assembly.Name, (Guid.NewGuid(), fileSystem.GetPath(projectFileName)));

                generator.Generate(assembly, typeDeclarationAnalysis, fileSystem);
            }

            WriteSolution(output, projectsByAssemblyName);

            return 0;
        }

        private static void WriteSolution(string output, IEnumerable<KeyValuePair<string, (Guid Id, string FullPath)>> projectsByAssemblyName)
        {
            using var slnWriter = new SlnWriter(File.CreateText(Path.Join(output, Path.GetFileName(output) + ".sln")));

            slnWriter.WriteHeader(
                visualStudioVersion: new Version("16.0.28701.123"),
                minimumVisualStudioVersion: new Version("10.0.40219.1"));

            var sdkCsprojProjectType = new Guid("9A19103F-16F7-4668-BE54-9A1E7A4F7556");

            var orderedProjectsByAssemblyName = projectsByAssemblyName.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToList();

            foreach (var (name, (id, fullPath)) in orderedProjectsByAssemblyName)
            {
                slnWriter.StartProject(sdkCsprojProjectType, name, Path.GetRelativePath(output, fullPath), id);
                slnWriter.EndProject();
            }

            slnWriter.StartGlobal();
            slnWriter.StartGlobalSection("SolutionConfigurationPlatforms", SlnGlobalSectionTiming.PreSolution);
            slnWriter.WriteGlobalSectionLine("Release|Any CPU = Release|Any CPU");
            slnWriter.EndGlobalSection();

            slnWriter.StartGlobalSection("ProjectConfigurationPlatforms", SlnGlobalSectionTiming.PostSolution);
            foreach (var (_, (id, _)) in orderedProjectsByAssemblyName)
            {
                slnWriter.WriteGlobalSectionLine("{" + id.ToString().ToUpperInvariant() + "}.Release|Any CPU.ActiveCfg = Release|Any CPU");
                slnWriter.WriteGlobalSectionLine("{" + id.ToString().ToUpperInvariant() + "}.Release|Any CPU.Build.0 = Release|Any CPU");
            }
            slnWriter.EndGlobalSection();
            slnWriter.EndGlobal();
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
    <NoWarn>CS0436;CS0618;CS0809;CS3005;IDE0034;IDE0060;IDE1006</NoWarn>
  </PropertyGroup>

</Project>
");
        }

        private static void WriteProjectFile(
            TextWriter writer,
            string targetFramework,
            ImmutableArray<string> assemblyReferences,
            ImmutableArray<PrimaryInteropAssembly> comReferences,
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

                foreach (var reference in assemblyReferences.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                {
                    writer.Write($@"
    <Reference Include=""{reference}"" />");
                }

                writer.Write(@"
  </ItemGroup>");
            }

            if (comReferences.Any())
            {
                writer.Write(@"

  <ItemGroup>");

                foreach (var reference in comReferences.OrderBy(r => r.AssemblyName.Name, StringComparer.OrdinalIgnoreCase))
                {
                    writer.Write($@"
    <COMReference Include=""{reference.AssemblyName.Name}"" Guid=""{reference.Guid}"" VersionMajor=""{reference.Version.Major}"" VersionMinor=""{reference.Version.Minor}"" />");
                }

                writer.Write(@"
  </ItemGroup>");
            }

            if (projectReferences.Any())
            {
                writer.Write(@"

  <ItemGroup>");

                foreach (var reference in projectReferences.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
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
