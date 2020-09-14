using System;
using System.IO;

namespace GenerateRefAssemblySource
{
    internal sealed partial class SlnWriter : IDisposable
    {
        private readonly TextWriter writer;
        private WriterState state;

        public SlnWriter(TextWriter writer) => this.writer = writer;

        public void Dispose() => writer.Dispose();

        public void WriteHeader(Version visualStudioVersion, Version minimumVisualStudioVersion)
        {
            AssertState(WriterState.Header);
            writer.WriteLine("Microsoft Visual Studio Solution File, Format Version 12.00");
            writer.Write("# Visual Studio Version ");
            writer.WriteLine(visualStudioVersion.Major);
            writer.Write("VisualStudioVersion = ");
            writer.WriteLine(visualStudioVersion);
            writer.Write("MinimumVisualStudioVersion  = ");
            writer.WriteLine(minimumVisualStudioVersion);
            state = WriterState.Body;
        }

        public void StartProject(Guid projectType, string projectName, string relativePath, Guid projectId)
        {
            AssertState(WriterState.Body);
            writer.Write("Project(");
            WriteGuid(projectType);
            writer.Write(") = ");
            WriteString(projectName);
            writer.Write(", ");
            WriteString(relativePath);
            writer.Write(", ");
            WriteGuid(projectId);
            writer.WriteLine();
            state = WriterState.Project;
        }

        public void EndProject()
        {
            AssertState(WriterState.Project);
            writer.WriteLine("EndProject");
            state = WriterState.Body;
        }

        public void StartGlobal()
        {
            AssertState(WriterState.Body);
            writer.WriteLine("Global");
            state = WriterState.Global;
        }

        public void EndGlobal()
        {
            AssertState(WriterState.Global);
            writer.WriteLine("EndGlobal");
            state = WriterState.Body;
        }

        public void StartGlobalSection(string name, SlnGlobalSectionTiming timing)
        {
            AssertState(WriterState.Global);
            writer.Write("\tGlobalSection(");
            writer.Write(name);
            writer.Write(") = ");
            writer.WriteLine(timing switch
            {
                SlnGlobalSectionTiming.PreSolution => "preSolution",
                SlnGlobalSectionTiming.PostSolution => "postSolution",
            });
            state = WriterState.GlobalSection;
        }

        public void EndGlobalSection()
        {
            AssertState(WriterState.GlobalSection);
            writer.WriteLine("\tEndGlobalSection");
            state = WriterState.Global;
        }

        public void WriteGlobalSectionLine(string contents)
        {
            AssertState(WriterState.GlobalSection);
            writer.Write("\t\t");
            writer.WriteLine(contents);
        }

        private void WriteGuid(Guid value)
        {
            writer.Write("\"{");
            writer.Write(value.ToString().ToUpperInvariant());
            writer.Write("}\"");
        }

        private void WriteString(string value)
        {
            if (value.Contains('"'))
                throw new NotImplementedException("TODO: Research whether double quotes can be escaped");

            writer.Write('"');
            writer.Write(value);
            writer.Write('"');
        }

        private void AssertState(WriterState requiredState)
        {
            if (state != requiredState)
                throw new InvalidOperationException($"The current state is {state}, but this operation is only valid in the {requiredState} state.");
        }
    }

    public enum SlnGlobalSectionTiming
    {
        PreSolution,
        PostSolution,
    }
}
