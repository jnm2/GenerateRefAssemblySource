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

        public void WriteProjectStart(Guid projectType, string projectName, string relativePath, Guid projectId)
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

        public void WriteProjectEnd()
        {
            AssertState(WriterState.Project);
            writer.WriteLine("EndProject");
            state = WriterState.Body;
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
}
