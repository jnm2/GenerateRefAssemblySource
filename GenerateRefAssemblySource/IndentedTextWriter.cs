using System;
using System.IO;
using System.Text;

namespace GenerateRefAssemblySource
{
    internal sealed class IndentedTextWriter : TextWriter
    {
        private readonly TextWriter writer;
        private readonly string indentString;
        private readonly bool leaveOpen;

        private bool didLineStart;

        private int indent;

        public int Indent
        {
            get => indent;
            set
            {
                if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "Indent must not be negative.");
                indent = value;
            }
        }

        public override Encoding Encoding => writer.Encoding;

        public IndentedTextWriter(TextWriter writer, string indentString = "    ", bool leaveOpen = false)
        {
            this.writer = writer;
            this.indentString = indentString;
            this.leaveOpen = leaveOpen;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen) writer.Dispose();
            base.Dispose(disposing);
        }

        public override void Write(char value)
        {
            if (value == '\n')
            {
                didLineStart = false;
            }
            else if (!didLineStart)
            {
                didLineStart = true;

                for (var i = 0; i < Indent; i++)
                    writer.Write(indentString);
            }

            writer.Write(value);
        }

        public override void WriteLine()
        {
            didLineStart = false;
            writer.WriteLine();
        }
    }
}
