using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace GenerateRefAssemblySource
{
    internal sealed class IndentedTextWriter : TextWriter
    {
        private readonly TextWriter writer;
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

        public IndentedTextWriter(TextWriter writer)
        {
            this.writer = writer;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) writer.Dispose();
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
                    writer.Write("    ");
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
