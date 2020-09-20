using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace GenerateRefAssemblySource
{
    public sealed partial class SourceGenerator
    {
        private readonly GenerationOptions options;

        public SourceGenerator(GenerationOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void Generate(IAssemblySymbol assembly, TypeDeclarationAnalysis typeDeclarationAnalysis, IProjectFileSystem fileSystem)
        {
            GenerateAssemblyAttributes(assembly, fileSystem);
            GenerateModuleAttributes(assembly, fileSystem);

            foreach (var (type, reason) in typeDeclarationAnalysis.ReasonsByType)
            {
                GenerateType(type, reason, typeDeclarationAnalysis, fileSystem);
            }
        }

        private void GenerateType(INamedTypeSymbol type, TypeDeclarationReason reason, TypeDeclarationAnalysis typeDeclarationAnalysis, IProjectFileSystem fileSystem)
        {
            using var textWriter = fileSystem.CreateText(GetPathForType(type));
            using var writer = new IndentedTextWriter(textWriter);

            var context = new GenerationContext(writer, type.ContainingNamespace);

            if (!type.ContainingNamespace.IsGlobalNamespace)
            {
                writer.Write("namespace ");
                context.WriteNamespace(type.ContainingNamespace);
                writer.WriteLine();
                writer.WriteLine('{');
                writer.Indent++;
            }

            var containingTypes = MetadataFacts.GetContainingTypes(type);
            foreach (var containingType in containingTypes)
            {
                WriteContainerTypeHeader(containingType, declareAsPartial: true, context);
                writer.WriteLine();
                writer.WriteLine('{');
                writer.Indent++;
            }

            var filteredAttributes = type.GetAttributes()
                .AddRange(PseudoCustomAttributeFacts.GenerateApiAttributes(type))
                .Where(a => a.AttributeClass?.HasFullName("System", "Reflection", "DefaultMemberAttribute") != true);

            if ((reason & (TypeDeclarationReason.ExternallyVisible | TypeDeclarationReason.DeclaresUsedAttribute)) != 0)
            {
                WriteAttributes(
                    filteredAttributes,
                    target: null,
                    context,
                    onlyWriteAttributeUsageAttribute: (reason & TypeDeclarationReason.ExternallyVisible) == 0);
            }

            WriteAccessibility(type.DeclaredAccessibility, reason, writer);

            var declareAsPartial = type.GetTypeMembers().Any(typeDeclarationAnalysis.ReasonsByType.ContainsKey);

            if (type.TypeKind == TypeKind.Delegate)
            {
                GenerateDelegate(type, context);
            }
            else if (type.TypeKind == TypeKind.Enum)
            {
                if (declareAsPartial) writer.Write("partial ");
                GenerateEnum(type, context);
            }
            else
            {
                if (type.TypeKind == TypeKind.Class)
                {
                    if (type.IsAbstract && type.IsSealed)
                        writer.Write("static ");

                    if (MetadataFacts.HidesBaseMember(type, typeDeclarationAnalysis))
                        writer.Write("new ");

                    if (type.IsSealed)
                        writer.Write("sealed ");
                    else if (type.IsAbstract)
                        writer.Write("abstract ");
                }

                WriteContainerTypeHeader(type, declareAsPartial, context);

                var generatedBaseType = type.BaseType;

                if (reason.HasFlag(TypeDeclarationReason.ExternallyVisible))
                {
                    var baseTypes = new List<INamedTypeSymbol>();

                    if (type.BaseType is { SpecialType: not (SpecialType.System_Object or SpecialType.System_ValueType) })
                        baseTypes.Add(type.BaseType);

                    baseTypes.AddRange(type.Interfaces.Where(i => MetadataFacts.IsVisibleOutsideAssembly(i)));

                    WriteBaseTypes(MetadataFacts.RemoveBaseTypes(baseTypes), context);
                }
                else if (reason.HasFlag(TypeDeclarationReason.DeclaresUsedAttribute))
                {
                    // Skip base types that are not visible
                    while (generatedBaseType is not null && !typeDeclarationAnalysis.IsVisibleOrInOtherAssembly(generatedBaseType))
                        generatedBaseType = generatedBaseType.BaseType;

                    if (generatedBaseType is not null)
                        WriteBaseTypes(new[] { generatedBaseType }, context);
                }
                else
                {
                    generatedBaseType = null;
                }

                WriteGenericParameterConstraints(type.TypeParameters, context);

                writer.WriteLine();
                writer.WriteLine('{');
                writer.Indent++;

                WriteTypeMembers(type, reason, generatedBaseType, typeDeclarationAnalysis, context);

                writer.Indent--;
                writer.WriteLine('}');
            }

            foreach (var containingType in containingTypes)
            {
                writer.Indent--;
                writer.WriteLine('}');
            }

            if (!type.ContainingNamespace.IsGlobalNamespace)
            {
                writer.Indent--;
                writer.WriteLine('}');
            }
        }

        private IEnumerable<ISymbol> FilterAndSortTypeMembers(IEnumerable<ISymbol> typeMembers)
        {
            return typeMembers
                .Select(m => (Member: m, SortKind: MetadataFacts.GetTypeMemberSortKind(m)))
                .Where(m => m.SortKind is not null)
                .OrderBy(m => options.TypeMemberOrder.IndexOf(m.SortKind!.Value))
                .ThenByDescending(m => m.Member.DeclaredAccessibility == Accessibility.Public)
                .ThenByDescending(m => m.Member.IsStatic)
                .ThenByDescending(m => (m.Member as IFieldSymbol)?.IsReadOnly)
                .ThenBy(m => m.Member.Name, StringComparer.OrdinalIgnoreCase)
                .Select(m => m.Member);
        }

        private void WriteTypeMembers(
            INamedTypeSymbol type,
            TypeDeclarationReason reason,
            INamedTypeSymbol? generatedBaseType,
            TypeDeclarationAnalysis typeDeclarationAnalysis,
            GenerationContext context)
        {
            var baseConstructorToCall = ((IMethodSymbol Constructor, bool SpecifyParameterTypes)?)null;

            if (options.GenerateRequiredBaseConstructorCalls
                && generatedBaseType is INamedTypeSymbol baseType
                && baseType.InstanceConstructors.Any()
                && !baseType.InstanceConstructors.Any(c => c.Parameters.IsEmpty))
            {
                var minParameterCount = baseType.InstanceConstructors.Min(c => c.Parameters.Length);
                var ctorsWithMinParameterCount = baseType.InstanceConstructors.Where(c => c.Parameters.Length == minParameterCount).ToList();

                baseConstructorToCall = (
                    Constructor: ctorsWithMinParameterCount.First(),
                    SpecifyParameterTypes: ctorsWithMinParameterCount.Count > 1);
            }

            var baseConstructorCallWasGenerated = false;

            var isFirst = true;

            var generatedMembers = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

            var sourceMembers = type.GetMembers().ToBuilder();

            for (var i = sourceMembers.Count - 1; i >= 0; i--)
            {
                if (sourceMembers[i] is IPropertySymbol { Parameters: { IsEmpty: false }, IsIndexer: false } property)
                {
                    sourceMembers.RemoveAt(i);

                    // C# can consume named parameterized properties in COM interfaces, but in other places C# can only
                    // consume as individual methods. Roslyn already has ordinary method symbols in
                    // ITypeSymbol.GetMembers() for the accessors in the later case, but we want to emit them in both
                    // cases since C# still can't declare named parameterized properties.
                    foreach (var accessor in new[] { property.GetMethod, property.SetMethod })
                    {
                        if (accessor is not null && !type.GetMembers(accessor.Name).Any(m => (m as IMethodSymbol)?.MethodKind == MethodKind.Ordinary))
                        {
                            sourceMembers.Add(accessor);
                        }
                    }
                }
            }

            foreach (var member in FilterAndSortTypeMembers(
                sourceMembers.Where(m =>
                {
                    if (!MetadataFacts.IsVisibleOutsideAssembly(m))
                    {
                        if (reason.HasFlag(TypeDeclarationReason.DeclaresUsedAttribute)
                            && m is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
                            && typeDeclarationAnalysis.IsUsedAttributeConstructor(constructor))
                        {
                            return true;
                        }

                        var isOverrideRequired =
                            options.GenerateRequiredProtectedOverridesInSealedClasses
                            && MetadataFacts.GetOverriddenMember(m) is { IsAbstract: true } overridden
                            && MetadataFacts.IsVisibleOutsideAssembly(overridden);

                        if (!isOverrideRequired) return false;
                    }

                    if (type.IsComImport
                        && type.TypeKind == TypeKind.Class
                        && m is IMethodSymbol { MethodKind: MethodKind.Constructor, Parameters: { IsEmpty: true } })
                    {
                        return false;
                    }

                    var isStructDefaultConstructor = type.TypeKind == TypeKind.Struct && m is IMethodSymbol { MethodKind: MethodKind.Constructor, Parameters: { IsEmpty: true } };

                    if (m.IsImplicitlyDeclared != isStructDefaultConstructor)
                        throw new NotImplementedException("Explicitly declared struct constructor or other kind of implicitly declared member");

                    return !isStructDefaultConstructor;
                })))
            {
                generatedMembers.Add(member);

                if (isFirst) isFirst = false; else context.Writer.WriteLine();

                if (member is IPropertySymbol { IsIndexer: true, MetadataName: not "Item" and var indexerName })
                {
                    context.Writer.Write("[System.Runtime.CompilerServices.IndexerName(");
                    context.WriteLiteral(indexerName);
                    context.Writer.WriteLine(")]");
                }

                var attributes = member.GetAttributes().AddRange(PseudoCustomAttributeFacts.GenerateApiAttributes(member));
                WriteAttributes(attributes, target: null, context);

                if (member is IMethodSymbol method)
                    WriteAttributes(method.GetReturnTypeAttributes(), target: "return", context);

                if (!(member.DeclaredAccessibility == Accessibility.Public && type.TypeKind == TypeKind.Interface))
                    WriteAccessibility(member.DeclaredAccessibility, TypeDeclarationReason.ExternallyVisible, context.Writer);

                var asExtern = member.IsExtern || type is { IsComImport: true, TypeKind: TypeKind.Class };

                if (member.Kind != SymbolKind.Field)
                {
                    if (member.IsStatic) context.Writer.Write("static ");
                    if (asExtern) context.Writer.Write("extern ");

                    if (member is not IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator or MethodKind.Conversion }
                        && MetadataFacts.HidesBaseMember(member, typeDeclarationAnalysis))
                    {
                        context.Writer.Write("new ");
                    }

                    if (member.IsVirtual) context.Writer.Write("virtual ");
                    if (member.IsAbstract && type.TypeKind != TypeKind.Interface) context.Writer.Write("abstract ");
                    if (member.IsSealed) context.Writer.Write("sealed ");
                    if (member.IsOverride) context.Writer.Write("override ");
                }

                switch (member)
                {
                    case IFieldSymbol f:
                        GenerateField(f, typeDeclarationAnalysis, context);
                        break;

                    case IEventSymbol e:
                        GenerateEvent(e, asExplicitImplementation: false, context);
                        break;

                    case IPropertySymbol p:
                        GenerateProperty(p, asExplicitImplementation: false, context);
                        break;

                    case IMethodSymbol m:
                        GenerateMethodHeader(m, asExplicitImplementation: false, context);

                        if (reason.HasFlag(TypeDeclarationReason.ExternallyVisible)
                            && m.MethodKind == MethodKind.Constructor
                            && baseConstructorToCall is var (baseConstructor, specifyParameterTypes))
                        {
                            GenerateBaseConstructorCall(baseConstructor, specifyParameterTypes, context);
                            baseConstructorCallWasGenerated = true;
                        }

                        WriteBody(context, GetBodyType(m, asExplicitImplementation: false, asExtern));
                        context.Writer.WriteLine();
                        break;

                    default:
                        throw new NotImplementedException();
                }
            }

            // These should be strictly synthesized. If actual members are used, they will pull along unnecessary
            // implementation details like attributes.

            if (!baseConstructorCallWasGenerated)
            {
                if (baseConstructorToCall is var (baseConstructor, specifyParameterTypes))
                {
                    if (isFirst) isFirst = false; else context.Writer.WriteLine();

                    context.Writer.Write("internal ");
                    context.WriteIdentifier(type.Name);
                    context.Writer.Write("()");
                    GenerateBaseConstructorCall(baseConstructor, specifyParameterTypes, context);
                    WriteBody(context, options.BodyOptions.RequiredBodyWithVoidReturn);
                    context.Writer.WriteLine();
                }
            }

            if (type.TypeKind != TypeKind.Interface && reason.HasFlag(TypeDeclarationReason.ExternallyVisible))
            {
                foreach (var interfaceMember in FilterAndSortTypeMembers(
                    type.AllInterfaces
                        .Except<INamedTypeSymbol>(generatedBaseType?.AllInterfaces ?? ImmutableArray<INamedTypeSymbol>.Empty, SymbolEqualityComparer.Default)
                        .Where(MetadataFacts.IsVisibleOutsideAssembly)
                        .SelectMany(i => i.GetMembers())
                        .Where(m => !generatedMembers.Contains(type.FindImplementationForInterfaceMember(m)!))))
                {
                    if (isFirst) isFirst = false; else context.Writer.WriteLine();

                    switch (interfaceMember)
                    {
                        case IEventSymbol e:
                            GenerateEvent(e, asExplicitImplementation: true, context);
                            break;

                        case IPropertySymbol p:
                            GenerateProperty(p, asExplicitImplementation: true, context);
                            break;

                        case IMethodSymbol m:
                            GenerateMethodHeader(m, asExplicitImplementation: true, context);
                            WriteBody(context, GetBodyType(m, asExplicitImplementation: true));
                            context.Writer.WriteLine();
                            break;

                        default:
                            throw new NotImplementedException("Implemented interface member that is not an event, property, or method.");
                    }
                }
            }
        }

        private void GenerateField(IFieldSymbol field, TypeDeclarationAnalysis typeDeclarationAnalysis, GenerationContext context)
        {
            if (field.IsConst)
            {
                context.Writer.Write("const ");
            }
            else
            {
                if (field.IsStatic) context.Writer.Write("static ");
                if (MetadataFacts.HidesBaseMember(field, typeDeclarationAnalysis)) context.Writer.Write("new ");
                if (field.IsReadOnly) context.Writer.Write("readonly ");
                if (field.Type.TypeKind == TypeKind.Pointer) context.Writer.Write("unsafe ");
                if (field.IsVolatile) context.Writer.Write("volatile ");
            }

            context.WriteTypeReference(field.Type);
            context.Writer.Write(' ');
            context.WriteIdentifier(field.Name);

            if (field.IsConst)
            {
                context.Writer.Write(" = ");
                context
                    .WithIsDefiningPrimitiveTypeConstant(MetadataFacts.IsPrimitiveType(field.ContainingType))
                    .WriteLiteral(field.Type, field.ConstantValue);
            }

            context.Writer.WriteLine(';');
        }

        private void GenerateEvent(IEventSymbol @event, bool asExplicitImplementation, GenerationContext context)
        {
            if (@event.RaiseMethod is { })
                throw new NotImplementedException("Raise accessor");

            context.Writer.Write("event ");
            context.WriteTypeReference(@event.Type);
            context.Writer.Write(' ');

            if (asExplicitImplementation)
            {
                context.WriteTypeReference(@event.ContainingType);
                context.Writer.Write('.');
            }

            context.WriteIdentifier(@event.Name);

            if (!asExplicitImplementation && (@event.IsAbstract || options.BodyOptions.UseFieldLikeEvents))
            {
                context.Writer.WriteLine(';');
            }
            else
            {
                context.Writer.Write(" { add");
                WriteBody(context, GetBodyType(@event.AddMethod!, asExplicitImplementation));
                context.Writer.Write(" remove");
                WriteBody(context, GetBodyType(@event.RemoveMethod!, asExplicitImplementation));
                context.Writer.WriteLine(" }");
            }
        }

        private void GenerateProperty(IPropertySymbol property, bool asExplicitImplementation, GenerationContext context)
        {
            if (property.Type.TypeKind == TypeKind.Pointer) context.Writer.Write("unsafe ");

            context.WriteTypeReference(property.Type);
            context.Writer.Write(' ');

            if (asExplicitImplementation)
            {
                context.WriteTypeReference(property.ContainingType);
                context.Writer.Write('.');
            }

            if (property.IsIndexer)
            {
                context.Writer.Write("this[");
                WriteParameterListContents(property.Parameters, asExplicitImplementation, context);
                context.Writer.Write(']');
            }
            else
            {
                context.WriteIdentifier(property.Name);
            }

            if (options.BodyOptions.UseExpressionBodiedPropertiesWhenThrowingNull
                && property is { GetMethod: not null, SetMethod: null }
                && GetBodyType(property.GetMethod, asExplicitImplementation) == GeneratedBodyType.ThrowNull)
            {
                WriteBody(context, GeneratedBodyType.ThrowNull);
            }
            else
            {
                context.Writer.Write(" { ");

                if (property.GetMethod is not null && MetadataFacts.IsVisibleOutsideAssembly(property.GetMethod))
                {
                    if (!asExplicitImplementation && property.GetMethod.DeclaredAccessibility != property.DeclaredAccessibility)
                    {
                        WriteAccessibility(property.GetMethod.DeclaredAccessibility, TypeDeclarationReason.ExternallyVisible, context.Writer);
                        context.Writer.Write(' ');
                    }

                    context.Writer.Write("get");
                    WriteBody(context, GetBodyType(property.GetMethod, asExplicitImplementation));
                    context.Writer.Write(' ');
                }

                if (property.SetMethod is not null && MetadataFacts.IsVisibleOutsideAssembly(property.SetMethod))
                {
                    if (!asExplicitImplementation && property.SetMethod.DeclaredAccessibility != property.DeclaredAccessibility)
                    {
                        WriteAccessibility(property.SetMethod.DeclaredAccessibility, TypeDeclarationReason.ExternallyVisible, context.Writer);
                        context.Writer.Write(' ');
                    }

                    context.Writer.Write("set");
                    WriteBody(context, GetBodyType(property.SetMethod, asExplicitImplementation));
                    context.Writer.Write(' ');
                }

                context.Writer.Write('}');
            }

            context.Writer.WriteLine();
        }

        private void GenerateMethodHeader(IMethodSymbol method, bool asExplicitImplementation, GenerationContext context)
        {
            if (method.ReturnType.TypeKind == TypeKind.Pointer || method.Parameters.Any(p => p.Type.TypeKind == TypeKind.Pointer))
                context.Writer.Write("unsafe ");

            switch (method.MethodKind)
            {
                case MethodKind.Ordinary:
                case MethodKind.PropertyGet: // Used to handle VB non-default indexers
                case MethodKind.PropertySet:
                    context.WriteTypeReference(method.ReturnType);
                    context.Writer.Write(' ');

                    if (asExplicitImplementation)
                    {
                        context.WriteTypeReference(method.ContainingType);
                        context.Writer.Write('.');
                    }

                    context.WriteIdentifier(method.Name);
                    break;

                case MethodKind.Constructor:
                case MethodKind.StaticConstructor:
                    context.WriteIdentifier(method.ContainingType.Name);
                    break;

                case MethodKind.UserDefinedOperator:
                    context.WriteTypeReference(method.ReturnType);
                    context.Writer.Write(" operator ");
                    SyntaxFactory.Token(SyntaxFacts.GetOperatorKind(method.Name)).WriteTo(context.Writer);
                    break;

                case MethodKind.Conversion:
                    context.Writer.Write(method.Name switch
                    {
                        WellKnownMemberNames.ImplicitConversionName => "implicit operator ",
                        WellKnownMemberNames.ExplicitConversionName => "explicit operator ",
                    });

                    context.WriteTypeReference(method.ReturnType);
                    break;

                default:
                    throw new NotImplementedException();
            }

            WriteGenericParameterList(method.TypeParameters, context);
            WriteParameterList(method, asExplicitImplementation, context);
            WriteGenericParameterConstraints(method.TypeParameters, context);
        }

        private void GenerateBaseConstructorCall(IMethodSymbol baseConstructor, bool specifyParameterTypes, GenerationContext context)
        {
            context.Writer.WriteLine();
            context.Writer.Indent++;

            context.Writer.Write(": base(");

            for (var i = 0; i < baseConstructor.Parameters.Length; i++)
            {
                if (i != 0) context.Writer.Write(", ");
                context.Writer.Write("default");

                if (specifyParameterTypes)
                {
                    context.Writer.Write('(');
                    context.WriteTypeReference(baseConstructor.Parameters[i].Type);
                    context.Writer.Write(')');
                }
            }

            context.Writer.Write(')');
            context.Writer.Indent--;
        }

        private GeneratedBodyType GetBodyType(IMethodSymbol method, bool asExplicitImplementation, bool asExtern = false)
        {
            return
                asExtern
                || (!asExplicitImplementation && method.IsAbstract)
                || (method.MethodKind is MethodKind.PropertyGet or MethodKind.PropertySet && options.BodyOptions.UseAutoProperties) ? GeneratedBodyType.None :
                method.ReturnsVoid ? options.BodyOptions.RequiredBodyWithVoidReturn :
                options.BodyOptions.RequiredBodyWithNonVoidReturn;
        }

        private void WriteBody(GenerationContext context, GeneratedBodyType type)
        {
            context.Writer.Write(type switch
            {
                GeneratedBodyType.None => ";",
                GeneratedBodyType.Empty => " { }",
                GeneratedBodyType.ThrowNull => " => throw null;",
            });
        }

        private static void WriteContainerTypeHeader(INamedTypeSymbol type, bool declareAsPartial, GenerationContext context)
        {
            if (declareAsPartial) context.Writer.Write("partial ");

            context.Writer.Write(type.TypeKind switch
            {
                TypeKind.Enum => "enum ",
                TypeKind.Struct => "struct ",
                TypeKind.Interface => "interface ",
                TypeKind.Class => "class ",
            });

            context.WriteIdentifier(type.Name);
            WriteGenericParameterList(type.TypeParameters, context);
        }

        private static void WriteBaseTypes(IReadOnlyCollection<INamedTypeSymbol> baseTypes, GenerationContext context)
        {
            using var enumerator = baseTypes
                .OrderByDescending(t => t.TypeKind == TypeKind.Class)
                .ThenByDescending(context.IsInCurrentNamespace)
                .ThenBy(t => t, NamespaceOrTypeFullNameComparer.Instance)
                .GetEnumerator();

            if (!enumerator.MoveNext()) return;

            context.Writer.Write(" :");

            var multiline = baseTypes.Count > 3;
            if (multiline) context.Writer.Indent++;

            while (true)
            {
                if (multiline)
                    context.Writer.WriteLine();
                else
                    context.Writer.Write(' ');

                context.WriteTypeReference(enumerator.Current);
                if (!enumerator.MoveNext()) break;
                context.Writer.Write(',');
            }

            if (multiline) context.Writer.Indent--;
        }

        private static void GenerateDelegate(INamedTypeSymbol type, GenerationContext context)
        {
            var invokeMethod = type.DelegateInvokeMethod!;

            if (invokeMethod.ReturnType.TypeKind == TypeKind.Pointer || invokeMethod.Parameters.Any(p => p.Type.TypeKind == TypeKind.Pointer))
                context.Writer.Write("unsafe ");

            context.Writer.Write("delegate ");
            context.WriteTypeReference(invokeMethod.ReturnType);
            context.Writer.Write(' ');
            context.WriteIdentifier(type.Name);

            WriteGenericParameterList(type.TypeParameters, context);
            WriteParameterList(invokeMethod, asExplicitImplementation: false, context);
            WriteGenericParameterConstraints(type.TypeParameters, context);

            context.Writer.WriteLine(';');
        }

        private static void GenerateEnum(INamedTypeSymbol type, GenerationContext context)
        {
            context.Writer.Write("enum ");
            context.WriteIdentifier(type.Name);

            if (type.EnumUnderlyingType!.SpecialType != SpecialType.System_Int32)
            {
                context.Writer.Write(" : ");
                context.WriteTypeReference(type.EnumUnderlyingType);
            }

            context.Writer.WriteLine();
            context.Writer.WriteLine('{');
            context.Writer.Indent++;

            foreach (var field in type.GetMembers().OfType<IFieldSymbol>()
                .Where(f => f.HasConstantValue)
                .OrderBy(f => f.ConstantValue)
                .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                WriteAttributes(field.GetAttributes(), target: null, context);
                context.WriteIdentifier(field.Name);
                context.Writer.Write(" = ");
                context.Writer.Write(field.ConstantValue);
                context.Writer.WriteLine(',');
            }

            context.Writer.Indent--;
            context.Writer.WriteLine('}');
        }

        private static void WriteGenericParameterList(ImmutableArray<ITypeParameterSymbol> typeParameters, GenerationContext context)
        {
            if (!typeParameters.Any()) return;

            context.Writer.Write('<');

            for (var i = 0; i < typeParameters.Length; i++)
            {
                if (i != 0) context.Writer.Write(", ");

                var genericParameter = typeParameters[i];

                if (genericParameter.GetAttributes().Any())
                    throw new NotImplementedException("Generic parameter attributes");

                context.Writer.Write(genericParameter.Variance switch
                {
                    VarianceKind.In => "in ",
                    VarianceKind.Out => "out ",
                    _ => null,
                });

                context.WriteIdentifier(genericParameter.Name);
            }

            context.Writer.Write('>');
        }

        private static void WriteGenericParameterConstraints(ImmutableArray<ITypeParameterSymbol> typeParameters, GenerationContext context)
        {
            context.Writer.Indent++;

            foreach (var typeParameter in typeParameters)
            {
                var mutuallyExclusiveInitialConstraintKeyword = new[]
                {
                    (Condition: typeParameter.HasReferenceTypeConstraint, Keyword: "class"),
                    (Condition: typeParameter.HasValueTypeConstraint, Keyword: "struct"),
                    (Condition: typeParameter.HasNotNullConstraint, Keyword: "notnull"),
                    (Condition: typeParameter.HasUnmanagedTypeConstraint, Keyword: "unmanaged"),
                }.SingleOrDefault(t => t.Condition).Keyword;

                if (mutuallyExclusiveInitialConstraintKeyword is null
                    && !typeParameter.HasConstructorConstraint
                    && !typeParameter.ConstraintTypes.Any())
                {
                    continue;
                }

                context.Writer.WriteLine();
                context.Writer.Write("where ");
                context.WriteIdentifier(typeParameter.Name);
                context.Writer.Write(" : ");

                if (mutuallyExclusiveInitialConstraintKeyword is { })
                    context.Writer.Write(mutuallyExclusiveInitialConstraintKeyword);

                var isFirst = mutuallyExclusiveInitialConstraintKeyword is null;

                for (var i = 0; i < typeParameter.ConstraintTypes.Length; i++)
                {
                    if (isFirst) isFirst = false; else context.Writer.Write(", ");
                    context.WriteTypeReference(typeParameter.ConstraintTypes[i]);
                }

                if (typeParameter.HasConstructorConstraint)
                {
                    if (!isFirst) context.Writer.Write(", ");
                    context.Writer.Write("new()");
                }
            }

            context.Writer.Indent--;
        }

        private static void WriteParameterList(IMethodSymbol method, bool asExplicitImplementation, GenerationContext context)
        {
            context.Writer.Write('(');
            WriteParameterListContents(method.Parameters, asExplicitImplementation, context);
            context.Writer.Write(')');
        }

        private static void WriteParameterListContents(ImmutableArray<IParameterSymbol> parameters, bool asExplicitImplementation, GenerationContext context)
        {
            var multiline = !asExplicitImplementation && (parameters.Length > 3 || parameters.SelectMany(p => p.GetAttributes()).Any());
            if (multiline)
            {
                context.Writer.WriteLine();
                context.Writer.Indent++;
            }

            var unrepresentableDefaultValueIndex = parameters.FindLastIndex(p => p is
            {
                HasExplicitDefaultValue: true,
                ExplicitDefaultValue: not null,
                Type: { IsReferenceType: true, SpecialType: not SpecialType.System_String },
            });

            for (var i = 0; i < parameters.Length; i++)
            {
                if (i != 0)
                {
                    if (multiline)
                        context.Writer.WriteLine(',');
                    else
                        context.Writer.Write(", ");
                }

                var parameter = parameters[i];

                var suppressEmittingAsOptional = asExplicitImplementation || i <= unrepresentableDefaultValueIndex;

                if (!suppressEmittingAsOptional && parameter.IsOptional && !parameter.HasExplicitDefaultValue)
                {
                    context.Writer.Write("[System.Runtime.InteropServices.Optional]");
                    if (multiline)
                        context.Writer.WriteLine();
                    else
                        context.Writer.Write(' ');
                }

                if (!asExplicitImplementation)
                    WriteAttributes(parameter.GetAttributes(), target: null, context);

                context.Writer.Write(parameter.RefKind switch
                {
                    RefKind.Ref => "ref ",
                    RefKind.Out => "out ",
                    RefKind.In => "in ",
                    _ => null,
                });

                context.WriteTypeReference(parameter.Type);
                context.Writer.Write(' ');
                context.WriteIdentifier(parameter.Name);

                if (!suppressEmittingAsOptional && parameter.HasExplicitDefaultValue)
                {
                    if (!parameter.IsOptional) throw new NotImplementedException();
                    context.Writer.Write(" = ");
                    context.WriteLiteral(parameter.Type, parameter.ExplicitDefaultValue);
                }
            }

            if (multiline) context.Writer.Indent--;
        }

        private static void WriteAccessibility(Accessibility accessibility, TypeDeclarationReason reason, TextWriter writer)
        {
            writer.Write(reason.HasFlag(TypeDeclarationReason.ExternallyVisible)
                ? accessibility switch
                {
                    Accessibility.Public => "public ",
                    Accessibility.Protected => "protected ",
                    Accessibility.ProtectedOrInternal => "protected ",
                }
                : accessibility switch
                {
                    Accessibility.Public => "public ",
                    Accessibility.Protected => "protected ",
                    Accessibility.ProtectedOrInternal => "protected internal ",
                    Accessibility.ProtectedAndInternal => "private protected ",
                    Accessibility.Internal => "internal ",
                    Accessibility.Private => "private ",
                });
        }

        private static string GetPathForType(INamedTypeSymbol type)
        {
            var typeAndContainingTypes = new List<INamedTypeSymbol>();

            for (var current = type; current is { }; current = current.ContainingType)
                typeAndContainingTypes.Add(current);

            typeAndContainingTypes.Reverse();

            var filename = new StringBuilder();

            foreach (var typeOrContainingType in typeAndContainingTypes)
            {
                filename.Append(typeOrContainingType.Name);

                if (typeOrContainingType.TypeParameters.Any())
                {
                    filename.Append('{');
                    filename.AppendJoin(',', typeOrContainingType.TypeParameters.Select(p => p.Name));
                    filename.Append('}');
                }

                filename.Append('.');
            }

            filename.Append("cs");

            return Path.Join(type.ContainingNamespace.Name?.Replace('.', Path.DirectorySeparatorChar), filename.ToString());
        }
    }
}
