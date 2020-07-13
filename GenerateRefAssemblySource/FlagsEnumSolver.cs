using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace GenerateRefAssemblySource
{
    internal readonly struct FlagsEnumSolver
    {
        private readonly ImmutableArray<(IFieldSymbol Field, ulong Value)> members;

        public FlagsEnumSolver(ITypeSymbol enumType)
        {
            members = enumType.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(f => f.HasConstantValue)
                .Select(f => (Field: f, Value: unchecked(f.ConstantValue switch
                {
                    int n => (ulong)n,
                    uint n => n,
                    short n => (ulong)n,
                    ushort n => n,
                    long n => (ulong)n,
                    ulong n => n,
                    sbyte n => (ulong)n,
                    byte n => n,
                })))
                .Where(m => m.Value != 0)
                .ToImmutableArray();
        }

        public Operation? Solve(ulong value)
        {
            return FindOrOperations(value) // Prefer OR operations
                ?? throw new NotImplementedException("TODO: search for flags operations besides 'or'");
        }

        private Operation? FindOrOperations(ulong value)
        {
            foreach (var member in members)
            {
                if ((member.Value & ~value) != 0) continue;

                // Prefer non-overlapping flags
                var remaining = value & ~member.Value;
                if (remaining == 0)
                    return new EnumMemberOperation(member.Field);

                if (Solve(remaining) is { } remainingOperation)
                    return CommutativeOperation.Or(new EnumMemberOperation(member.Field), remainingOperation);

                // TODO: overlapping flags
            }

            return null;
        }

        public abstract class Operation
        {
            public abstract void Write(GenerationContext generationContext);
        }

        public sealed class CommutativeOperation : Operation
        {
            private CommutativeOperation(CommutativeOperationKind kind, ImmutableArray<Operation> operands)
            {
                if (operands.IsDefault || operands.Length < 2)
                    throw new ArgumentException("At least two operands must be specified.", nameof(operands));

                Kind = kind;
                Operands = operands;
            }

            public CommutativeOperationKind Kind { get; }
            public ImmutableArray<Operation> Operands { get; }

            public static CommutativeOperation Or(params Operation[] operands)
            {
                var flattened = ImmutableArray.CreateBuilder<Operation>();

                foreach (var operand in operands)
                {
                    if (operand is CommutativeOperation { Kind: CommutativeOperationKind.Or, Operands: var innerOperands })
                        flattened.AddRange(innerOperands);
                    else
                        flattened.Add(operand);
                }

                return new CommutativeOperation(CommutativeOperationKind.Or, flattened.ToImmutable());
            }

            public override void Write(GenerationContext generationContext)
            {
                var operatorText = Kind switch
                {
                    CommutativeOperationKind.Or => " | ",
                    CommutativeOperationKind.And => " & ",
                    CommutativeOperationKind.Xor => " ^ ",
                };

                for (var i = 0; i < Operands.Length; i++)
                {
                    if (i != 0) generationContext.Writer.Write(operatorText);
                    Operands[i].Write(generationContext);
                }
            }
        }

        public enum CommutativeOperationKind
        {
            Or,
            And,
            Xor,
        }

        public sealed class NotOperation : Operation
        {
            public NotOperation(Operation operand)
            {
                Operand = operand;
            }

            public Operation Operand { get; }

            public override void Write(GenerationContext generationContext)
            {
                generationContext.Writer.Write('~');
                if (Operand is CommutativeOperation) generationContext.Writer.Write('(');
                Operand.Write(generationContext);
                if (Operand is CommutativeOperation) generationContext.Writer.Write(')');
            }
        }

        public sealed class EnumMemberOperation : Operation
        {
            public EnumMemberOperation(IFieldSymbol field)
            {
                Field = field;
            }

            public IFieldSymbol Field { get; }

            public override void Write(GenerationContext generationContext)
            {
                generationContext.WriteTypeReference(Field.ContainingType);
                generationContext.Writer.Write('.');
                generationContext.WriteIdentifier(Field.Name);
            }
        }
    }
}
