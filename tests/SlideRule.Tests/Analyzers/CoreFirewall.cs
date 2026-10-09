using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace SlideRule.Tests.Analyzers;

/// <summary>One breach of the firewall: where it is, the rule it breaks, and what it names.</summary>
/// <param name="Site">The method or type at fault, by its metadata name.</param>
/// <param name="Rule">The rule broken, as one of the phrases <see cref="CoreFirewall" /> declares.</param>
/// <param name="Detail">What the site names, calls or hands over that breaks the rule.</param>
internal sealed record FirewallBreach(string Site, string Rule, string Detail)
{
    /// <inheritdoc />
    public override string ToString()
    {
        return $"{Site}: {Rule}: {Detail}";
    }
}

/// <summary>
///     Reads an analyzer assembly's IL and reports every place where a copy of SlideRule or
///     SlideRule.Extraction that will not bind could reach the compiler as something other than the
///     analyzer's own fault warning.
/// </summary>
/// <remarks>
///     <para>
///         The JIT binds a method's references when it compiles the method, and a reference that will not
///         bind throws at that method's call site in its caller. So the rules are about where references
///         sit, not about what runs. A boundary is a method carrying <c>NoInlining</c>, a method of a state
///         machine whose kickoff is one, or a compiler-generated lambda or local function whose every
///         reference sits in a boundary and whose delegate Roslyn is never handed.
///     </para>
///     <list type="number">
///         <item>
///             A method that is no boundary names nothing on the far side: no type, member, instantiation,
///             signature or local of theirs, and no member of this assembly whose signature or field type
///             does, nor a type of this assembly whose loading binds one (rule 5's test).
///         </item>
///         <item>
///             A method that is no boundary calls or takes the address of a boundary only inside a try of
///             its own whose handler catches <c>System.Exception</c>, as a catch clause or as a filter
///             testing for it.
///         </item>
///         <item>
///             Roslyn is never handed a boundary: not as a registered action, not as a value provider's
///             compute function, and not as an analyzer member it calls itself. A delegate whose target the
///             walk cannot follow is reported too, because it cannot be shown to hold.
///         </item>
///         <item>
///             The fault path names nothing on the far side: the named fault methods, every static
///             constructor, and everything either calls.
///         </item>
///         <item>
///             No type's fields make loading it bind the far side: no field of one of their value types or
///             of a value-type instantiation over their types, and no static field naming them at all. A type
///             of this assembly whose loading binds the far side, through such a field, its base type or an
///             interface, is itself a reference to the far side wherever rule 1 finds it named.
///         </item>
///     </list>
///     <para>
///         What IL cannot show is not claimed: a boundary iterator enumerated outside the guard of the
///         method that called it, or a delegate to a boundary invoked somewhere its creator's guard does not
///         reach, both pass. The walk follows delegates through the stack, locals and fields of one method
///         and through fields across the assembly, which is every shape the C# compiler emits for a lambda
///         or a method group handed straight to a call.
///     </para>
/// </remarks>
internal static class CoreFirewall
{
    /// <summary>Rule 1 as a breach reads.</summary>
    internal const string NamesCore = "rule 1, names Core outside a boundary";

    /// <summary>Rule 2 as a breach reads.</summary>
    internal const string CallsUnguarded = "rule 2, calls a boundary outside a guard";

    /// <summary>Rule 3 as a breach reads.</summary>
    internal const string HandsRoslyn = "rule 3, hands Roslyn a boundary";

    /// <summary>Rule 4 as a breach reads.</summary>
    internal const string FaultPathNamesCore = "rule 4, the fault path names Core";

    /// <summary>Rule 5 as a breach reads.</summary>
    internal const string LoadBindsCore = "rule 5, loading the type binds Core";

    /// <summary>The assemblies on the far side of the firewall.</summary>
    private static readonly ImmutableHashSet<string> Guarded =
        ImmutableHashSet.Create(StringComparer.Ordinal, "SlideRule", "SlideRule.Extraction");

    /// <summary>Every breach in the built <paramref name="assembly" />, in site order.</summary>
    /// <param name="assembly">The analyzer assembly, read from where it was loaded.</param>
    /// <param name="faultPath">
    ///     The fault path's own methods, each written <c>Namespace.Type.Method</c> with nested types
    ///     joined by <c>+</c>; every static constructor is added to them.
    /// </param>
    internal static IReadOnlyList<FirewallBreach> Inspect(Assembly assembly, IReadOnlyList<string> faultPath)
    {
        ImmutableArray<byte> image = ImmutableArray.Create(File.ReadAllBytes(assembly.Location));

        return Inspect(image, faultPath);
    }

    /// <summary>The same over an image held in memory, such as a compilation emitted to a stream.</summary>
    /// <param name="image">The assembly's bytes.</param>
    /// <param name="faultPath">The fault path's own methods, written as for the other overload.</param>
    internal static IReadOnlyList<FirewallBreach> Inspect(ImmutableArray<byte> image, IReadOnlyList<string> faultPath)
    {
        using var pe = new PEReader(image);
        var walk = new Walk(pe);

        return walk.Breaches(faultPath);
    }

    /// <summary>The far-side assemblies <paramref name="image" /> references at all, by name.</summary>
    internal static IReadOnlyList<string> GuardedReferences(ImmutableArray<byte> image)
    {
        using var pe = new PEReader(image);
        MetadataReader metadata = pe.GetMetadataReader();

        return metadata.AssemblyReferences
            .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
            .Where(Guarded.Contains)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>One IL instruction: its offset, its opcode, its operand and where control can go next.</summary>
    private readonly record struct Instruction(int Offset, OpCode OpCode, int Operand, int[] Targets, int Next)
    {
        /// <summary>Whether the operand is a metadata token.</summary>
        internal bool CarriesToken => OpCode.OperandType is OperandType.InlineField or OperandType.InlineMethod
            or OperandType.InlineSig or OperandType.InlineTok or OperandType.InlineType;

        /// <summary>Whether the operand names a method: a call, a construction or a function pointer.</summary>
        internal bool NamesMethod => OpCode.OperandType == OperandType.InlineMethod;
    }

    /// <summary>
    ///     A type as a signature spells it: its display name, whether it is a value type, the far-side types
    ///     it mentions anywhere in it, and the types of the walked assembly it mentions.
    /// </summary>
    private sealed record Shape(
        string Name,
        bool IsValueType,
        bool IsGuarded,
        ImmutableHashSet<string> Core,
        ImmutableHashSet<TypeDefinitionHandle> Own)
    {
        internal static readonly ImmutableHashSet<string> NoCore = ImmutableHashSet.Create<string>(StringComparer.Ordinal);

        internal static readonly ImmutableHashSet<TypeDefinitionHandle> NoOwn = [];
    }

    /// <summary>A method of the walked assembly and everything the rules read off it.</summary>
    private sealed class Method
    {
        internal Method(Walk walk, MethodDefinitionHandle handle)
        {
            Handle = handle;
            Definition = walk.Metadata.GetMethodDefinition(handle);
            Name = walk.Metadata.GetString(Definition.Name);
            Display = walk.NameOf(Definition.GetDeclaringType()) + "." + Name;
            Body = Definition.RelativeVirtualAddress == 0 ? null : walk.Pe.GetMethodBody(Definition.RelativeVirtualAddress);
            Instructions = Body is null ? [] : Decode(Body);
        }

        internal MethodDefinitionHandle Handle { get; }

        internal MethodDefinition Definition { get; }

        internal string Name { get; }

        internal string Display { get; }

        internal MethodBodyBlock? Body { get; }

        internal IReadOnlyList<Instruction> Instructions { get; }

        /// <summary>The far-side names this method's metadata carries, one line each.</summary>
        internal SortedSet<string> Named { get; set; } = new(StringComparer.Ordinal);

        /// <summary>The methods that call it or take its address.</summary>
        internal HashSet<MethodDefinitionHandle> Sites { get; } = [];

        /// <summary>Whether Roslyn is handed it as a delegate, or calls it as a member of an analyzer.</summary>
        internal bool ReachedByRoslyn { get; set; }

        /// <summary>Whether Roslyn calls it as a member of an analyzer or a provider: an override, or the constructor.</summary>
        internal bool IsRoslynMember { get; set; }

        /// <summary>Whether it is a boundary, as decided over the whole assembly.</summary>
        internal bool IsBoundary { get; set; }

        // MethodImplAttributes carries no [Flags] attribute, though NoInlining is a single bit of it.
        internal bool IsDeclaredBoundary => ((int)Definition.ImplAttributes & (int)MethodImplAttributes.NoInlining) != 0;

        /// <summary>A lambda or local function the compiler generated.</summary>
        internal bool IsClosure => Name.StartsWith('<') && (Name.Contains(">b__") || Name.Contains(">g__"));
    }

    // Every opcode by its encoded value, with the operand shape and the stack behaviour Reflection.Emit
    // records for it.
    private static readonly IReadOnlyDictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .GroupBy(code => unchecked((ushort)code.Value))
        .ToDictionary(group => group.Key, group => group.First());

    private static List<Instruction> Decode(MethodBodyBlock body)
    {
        BlobReader il = body.GetILReader();
        var instructions = new List<Instruction>();
        while (il.RemainingBytes > 0)
        {
            int offset = il.Offset;
            ushort value = il.ReadByte();
            // A 0xFE byte opens a two-byte opcode, which the table keys by both bytes.
            if (value == 0xFE) value = (ushort)(0xFE00 | il.ReadByte());

            OpCode code = OpCodesByValue[value];
            var operand = 0;
            int[] targets = [];
            switch (code.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                    sbyte shortJump = il.ReadSByte();
                    targets = [il.Offset + shortJump];
                    break;
                case OperandType.InlineBrTarget:
                    int jump = il.ReadInt32();
                    targets = [il.Offset + jump];
                    break;
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    operand = il.ReadByte();
                    break;
                case OperandType.InlineVar:
                    operand = il.ReadUInt16();
                    break;
                case OperandType.InlineI:
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                    operand = il.ReadInt32();
                    break;
                case OperandType.ShortInlineR:
                    il.ReadSingle();
                    break;
                case OperandType.InlineI8:
                    il.ReadInt64();
                    break;
                case OperandType.InlineR:
                    il.ReadDouble();
                    break;
                case OperandType.InlineSwitch:
                    int count = il.ReadInt32();
                    var jumps = new int[count];
                    for (var index = 0; index < count; index++) jumps[index] = il.ReadInt32();
                    int end = il.Offset;
                    targets = jumps.Select(entry => end + entry)
                        .ToArray();
                    break;
                default:
                    throw new InvalidOperationException($"The walk cannot read the operand of {code.Name}.");
            }

            instructions.Add(new Instruction(offset, code, operand, targets, il.Offset));
        }

        return instructions;
    }

    /// <summary>The stack slots and locals of one point in a method, each the set of methods a delegate there could target.</summary>
    private sealed class Frame(
        ImmutableList<ImmutableHashSet<int>> stack,
        ImmutableDictionary<int, ImmutableHashSet<int>> locals)
    {
        internal static readonly ImmutableHashSet<int> Nothing = [];

        internal static readonly Frame Empty = new([], ImmutableDictionary<int, ImmutableHashSet<int>>.Empty);

        internal ImmutableList<ImmutableHashSet<int>> Stack { get; } = stack;

        internal ImmutableDictionary<int, ImmutableHashSet<int>> Locals { get; } = locals;

        /// <summary>This frame joined with <paramref name="other" />, or null where that adds nothing.</summary>
        internal Frame? Joined(Frame other)
        {
            if (Stack.Count != other.Stack.Count)
                throw new InvalidOperationException("The walk met two stack depths at one instruction.");

            var grew = false;
            ImmutableList<ImmutableHashSet<int>>.Builder stack = Stack.ToBuilder();
            for (var index = 0; index < stack.Count; index++)
            {
                ImmutableHashSet<int> union = stack[index].Union(other.Stack[index]);
                if (union.Count == stack[index].Count) continue;

                stack[index] = union;
                grew = true;
            }

            ImmutableDictionary<int, ImmutableHashSet<int>> locals = Locals;
            foreach (KeyValuePair<int, ImmutableHashSet<int>> local in other.Locals)
            {
                ImmutableHashSet<int> held = locals.GetValueOrDefault(local.Key, Nothing);
                ImmutableHashSet<int> union = held.Union(local.Value);
                if (union.Count == held.Count && locals.ContainsKey(local.Key)) continue;

                locals = locals.SetItem(local.Key, union);
                grew = true;
            }

            return grew ? new Frame(stack.ToImmutable(), locals) : null;
        }
    }

    // A type argument reader for the one custom attribute the walk decodes, the state machine attribute,
    // whose argument is a type written by its serialized name.
    private sealed class SerializedNames(Walk walk) : ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            return typeCode.ToString();
        }

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            return walk.NameOf(handle);
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            return walk.NameOf(handle);
        }

        public string GetSZArrayType(string elementType)
        {
            return elementType + "[]";
        }

        public string GetSystemType()
        {
            return "System.Type";
        }

        public bool IsSystemType(string type)
        {
            return type == "System.Type";
        }

        public string GetTypeFromSerializedName(string name)
        {
            return name;
        }

        public PrimitiveTypeCode GetUnderlyingEnumType(string type)
        {
            return PrimitiveTypeCode.Int32;
        }
    }

    // Reads signatures into shapes, marking every far-side type they mention.
    private sealed class Shapes(Walk walk) : ISignatureTypeProvider<Shape, object?>
    {
        public Shape GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            bool isValueType = typeCode is not (PrimitiveTypeCode.String or PrimitiveTypeCode.Object or PrimitiveTypeCode.Void);

            return new Shape(typeCode.ToString(), isValueType, false, Shape.NoCore, Shape.NoOwn);
        }

        public Shape GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            bool isValueType = rawTypeKind == (byte)SignatureTypeKind.ValueType || walk.IsValueType(handle);

            return new Shape(walk.NameOf(handle), isValueType, false, Shape.NoCore, Shape.NoOwn.Add(handle));
        }

        public Shape GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            string name = walk.NameOf(handle);
            bool guarded = walk.IsGuarded(handle);
            ImmutableHashSet<string> core = guarded ? Shape.NoCore.Add(name) : Shape.NoCore;

            return new Shape(name, rawTypeKind == (byte)SignatureTypeKind.ValueType, guarded, core, Shape.NoOwn);
        }

        public Shape GetTypeFromSpecification(
            MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        {
            return reader.GetTypeSpecification(handle)
                .DecodeSignature(this, genericContext);
        }

        public Shape GetSZArrayType(Shape elementType)
        {
            return new Shape(elementType.Name + "[]", false, false, elementType.Core, elementType.Own);
        }

        public Shape GetArrayType(Shape elementType, ArrayShape shape)
        {
            string rank = new(',', shape.Rank - 1);

            return new Shape(elementType.Name + "[" + rank + "]", false, false, elementType.Core, elementType.Own);
        }

        public Shape GetByReferenceType(Shape elementType)
        {
            return new Shape(elementType.Name + "&", false, false, elementType.Core, elementType.Own);
        }

        public Shape GetPointerType(Shape elementType)
        {
            return new Shape(elementType.Name + "*", false, false, elementType.Core, elementType.Own);
        }

        public Shape GetPinnedType(Shape elementType)
        {
            return elementType;
        }

        public Shape GetModifiedType(Shape modifier, Shape unmodifiedType, bool isRequired)
        {
            return unmodifiedType with { Core = unmodifiedType.Core.Union(modifier.Core) };
        }

        public Shape GetGenericInstantiation(Shape genericType, ImmutableArray<Shape> typeArguments)
        {
            int tick = genericType.Name.IndexOf('`');
            string name = tick < 0 ? genericType.Name : genericType.Name.Substring(0, tick);
            IEnumerable<string> arguments = typeArguments.Select(argument => argument.Name);
            ImmutableHashSet<string> core = typeArguments.Aggregate(
                genericType.Core, (union, argument) => union.Union(argument.Core));
            ImmutableHashSet<TypeDefinitionHandle> own = typeArguments.Aggregate(
                genericType.Own, (union, argument) => union.Union(argument.Own));

            return new Shape(
                name + "<" + string.Join(", ", arguments) + ">", genericType.IsValueType, genericType.IsGuarded,
                core, own);
        }

        public Shape GetGenericTypeParameter(object? genericContext, int index)
        {
            return new Shape("!" + index, false, false, Shape.NoCore, Shape.NoOwn);
        }

        public Shape GetGenericMethodParameter(object? genericContext, int index)
        {
            return new Shape("!!" + index, false, false, Shape.NoCore, Shape.NoOwn);
        }

        public Shape GetFunctionPointerType(MethodSignature<Shape> signature)
        {
            ImmutableArray<Shape> parts = signature.ParameterTypes.Add(signature.ReturnType);
            ImmutableHashSet<string> core = parts.Aggregate(Shape.NoCore, (union, part) => union.Union(part.Core));
            ImmutableHashSet<TypeDefinitionHandle> own = parts.Aggregate(Shape.NoOwn, (union, part) => union.Union(part.Own));

            return new Shape("method", false, false, core, own);
        }
    }

    /// <summary>One pass over one assembly.</summary>
    private sealed class Walk
    {
        private static readonly string[] StateMachineAttributes =
        [
            "System.Runtime.CompilerServices.IteratorStateMachineAttribute",
            "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
            "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute"
        ];

        private readonly Shapes _shapes;
        private readonly Dictionary<MethodDefinitionHandle, Method> _methods = [];

        // The walked assembly's types whose loading binds the far side, each with the reason.
        private readonly Dictionary<TypeDefinitionHandle, string> _binding = [];

        // Each state machine type, and the method that creates it.
        private readonly Dictionary<TypeDefinitionHandle, MethodDefinitionHandle> _kickoffs = [];

        // Every delegate target stored in each field, across the whole assembly.
        private readonly Dictionary<int, ImmutableHashSet<int>> _stored = [];

        internal Walk(PEReader pe)
        {
            Pe = pe;
            Metadata = pe.GetMetadataReader();
            _shapes = new Shapes(this);

            foreach (MethodDefinitionHandle handle in Metadata.MethodDefinitions)
                _methods.Add(handle, new Method(this, handle));

            MapStateMachines();
            FindBindingTypes();
            foreach (Method method in _methods.Values) method.Named = NamedBy(method);

            MapSites();
        }

        internal PEReader Pe { get; }

        internal MetadataReader Metadata { get; }

        /// <summary>Every breach the five rules find, ordered by site and rule.</summary>
        internal IReadOnlyList<FirewallBreach> Breaches(IReadOnlyList<string> faultPath)
        {
            var breaches = new List<FirewallBreach>();

            MarkRoslynMembers();
            CollectStoredDelegates();
            foreach (Method method in _methods.Values) CheckReceipts(method, breaches);
            DecideBoundaries();

            foreach (Method method in _methods.Values)
            {
                if (method.IsBoundary)
                {
                    if (method.IsDeclaredBoundary && method.IsRoslynMember)
                        breaches.Add(new FirewallBreach(method.Display, HandsRoslyn, "Roslyn calls it directly"));

                    continue;
                }

                if (method.Named.Count > 0)
                    breaches.Add(new FirewallBreach(method.Display, NamesCore, string.Join(", ", method.Named)));

                List<string> unguarded = UnguardedBoundaryCalls(method);
                if (unguarded.Count > 0)
                    breaches.Add(new FirewallBreach(method.Display, CallsUnguarded, string.Join(", ", unguarded)));
            }

            CheckFaultPath(faultPath, breaches);
            CheckTypeLoads(breaches);

            return breaches
                .OrderBy(breach => breach.Site, StringComparer.Ordinal)
                .ThenBy(breach => breach.Rule, StringComparer.Ordinal)
                .ThenBy(breach => breach.Detail, StringComparer.Ordinal)
                .ToList();
        }

        internal string NameOf(TypeDefinitionHandle handle)
        {
            TypeDefinition type = Metadata.GetTypeDefinition(handle);
            string name = Metadata.GetString(type.Name);
            TypeDefinitionHandle declaring = type.GetDeclaringType();
            if (!declaring.IsNil) return NameOf(declaring) + "+" + name;

            string space = Metadata.GetString(type.Namespace);

            return space.Length == 0 ? name : space + "." + name;
        }

        internal string NameOf(TypeReferenceHandle handle)
        {
            TypeReference type = Metadata.GetTypeReference(handle);
            string name = Metadata.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
                return NameOf((TypeReferenceHandle)type.ResolutionScope) + "+" + name;

            string space = Metadata.GetString(type.Namespace);

            return space.Length == 0 ? name : space + "." + name;
        }

        internal bool IsGuarded(TypeReferenceHandle handle)
        {
            EntityHandle scope = Metadata.GetTypeReference(handle).ResolutionScope;
            while (scope.Kind == HandleKind.TypeReference)
                scope = Metadata.GetTypeReference((TypeReferenceHandle)scope).ResolutionScope;

            if (scope.Kind != HandleKind.AssemblyReference) return false;

            AssemblyReference assembly = Metadata.GetAssemblyReference((AssemblyReferenceHandle)scope);

            return Guarded.Contains(Metadata.GetString(assembly.Name));
        }

        internal bool IsValueType(TypeDefinitionHandle handle)
        {
            EntityHandle baseType = Metadata.GetTypeDefinition(handle).BaseType;
            if (baseType.Kind != HandleKind.TypeReference) return false;

            string name = NameOf((TypeReferenceHandle)baseType);

            return name is "System.ValueType" or "System.Enum";
        }

        private Shape ShapeOf(EntityHandle handle)
        {
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => _shapes.GetTypeFromDefinition(Metadata, (TypeDefinitionHandle)handle, 0),
                HandleKind.TypeReference => _shapes.GetTypeFromReference(Metadata, (TypeReferenceHandle)handle, 0),
                HandleKind.TypeSpecification =>
                    _shapes.GetTypeFromSpecification(Metadata, null, (TypeSpecificationHandle)handle, 0),
                _ => throw new InvalidOperationException($"The walk cannot read a type from a {handle.Kind}.")
            };
        }

        private MethodSignature<Shape> SignatureOf(EntityHandle handle)
        {
            return handle.Kind switch
            {
                HandleKind.MethodDefinition => Metadata.GetMethodDefinition((MethodDefinitionHandle)handle)
                    .DecodeSignature(_shapes, null),
                HandleKind.MemberReference => Metadata.GetMemberReference((MemberReferenceHandle)handle)
                    .DecodeMethodSignature(_shapes, null),
                HandleKind.MethodSpecification =>
                    SignatureOf(Metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method),
                _ => throw new InvalidOperationException($"The walk cannot read a method from a {handle.Kind}.")
            };
        }

        // The method of this assembly a token names, where it names one: directly, through an instantiation,
        // or through a reference to a member of one of this assembly's generic types.
        private MethodDefinitionHandle? OwnMethod(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                    return (MethodDefinitionHandle)handle;
                case HandleKind.MethodSpecification:
                    return OwnMethod(Metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
                case HandleKind.MemberReference:
                    MemberReference member = Metadata.GetMemberReference((MemberReferenceHandle)handle);
                    if (member.GetKind() != MemberReferenceKind.Method) return null;
                    if (OwnTypeOf(member.Parent) is not { } owner) return null;

                    string name = Metadata.GetString(member.Name);
                    int arity = member.DecodeMethodSignature(_shapes, null).ParameterTypes.Length;
                    List<MethodDefinitionHandle> matches = Metadata.GetTypeDefinition(owner)
                        .GetMethods()
                        .Where(candidate => _methods[candidate].Name == name
                                            && _methods[candidate].Definition.DecodeSignature(_shapes, null)
                                                .ParameterTypes.Length == arity)
                        .ToList();

                    return matches.Count == 1 ? matches[0] : null;
                default:
                    return null;
            }
        }

        private TypeDefinitionHandle? OwnTypeOf(EntityHandle parent)
        {
            if (parent.Kind == HandleKind.TypeDefinition) return (TypeDefinitionHandle)parent;
            if (parent.Kind != HandleKind.TypeSpecification) return null;

            BlobReader blob = Metadata.GetBlobReader(Metadata.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
            if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance) return null;

            blob.ReadSignatureTypeCode();
            EntityHandle generic = blob.ReadTypeHandle();

            return generic.Kind == HandleKind.TypeDefinition ? (TypeDefinitionHandle)generic : null;
        }

        // The namespace and name of the type a member reference's parent is, or of its generic definition.
        private (string Namespace, string Name)? ParentName(EntityHandle parent)
        {
            EntityHandle type = parent;
            if (parent.Kind == HandleKind.TypeSpecification)
            {
                BlobReader blob = Metadata.GetBlobReader(Metadata.GetTypeSpecification((TypeSpecificationHandle)parent).Signature);
                if (blob.ReadSignatureTypeCode() != SignatureTypeCode.GenericTypeInstance) return null;

                blob.ReadSignatureTypeCode();
                type = blob.ReadTypeHandle();
            }

            if (type.Kind != HandleKind.TypeReference) return null;

            TypeReference reference = Metadata.GetTypeReference((TypeReferenceHandle)type);

            return (Metadata.GetString(reference.Namespace), Metadata.GetString(reference.Name));
        }

        private void MapStateMachines()
        {
            Dictionary<string, TypeDefinitionHandle> byName = Metadata.TypeDefinitions
                .ToDictionary(NameOf, handle => handle, StringComparer.Ordinal);
            var serialized = new SerializedNames(this);

            foreach (Method method in _methods.Values)
            foreach (CustomAttributeHandle handle in method.Definition.GetCustomAttributes())
            {
                CustomAttribute attribute = Metadata.GetCustomAttribute(handle);
                if (!StateMachineAttributes.Contains(AttributeName(attribute), StringComparer.Ordinal)) continue;

                CustomAttributeValue<string> value = attribute.DecodeValue(serialized);
                if (value.FixedArguments[0].Value is not string machine) continue;
                if (!byName.TryGetValue(machine, out TypeDefinitionHandle type)) continue;

                _kickoffs[type] = method.Handle;
            }
        }

        private string AttributeName(CustomAttribute attribute)
        {
            EntityHandle constructor = attribute.Constructor;
            EntityHandle type = constructor.Kind == HandleKind.MethodDefinition
                ? Metadata.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType()
                : Metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent;

            return type.Kind switch
            {
                HandleKind.TypeReference => NameOf((TypeReferenceHandle)type),
                HandleKind.TypeDefinition => NameOf((TypeDefinitionHandle)type),
                _ => string.Empty
            };
        }

        private MethodDefinitionHandle? KickoffOf(Method method)
        {
            TypeDefinitionHandle type = method.Definition.GetDeclaringType();

            return _kickoffs.TryGetValue(type, out MethodDefinitionHandle kickoff) ? kickoff : null;
        }

        // A type of this assembly binds the far side when a field's layout, its base type or an interface
        // does, and that can be through another of its own types, so the set grows until nothing new joins it.
        private void FindBindingTypes()
        {
            bool grew;
            do
            {
                grew = false;
                foreach (TypeDefinitionHandle handle in Metadata.TypeDefinitions)
                {
                    if (_binding.ContainsKey(handle)) continue;

                    string? cause = FieldCauses(handle, false)
                        .Concat(ShapeCauses(handle))
                        .FirstOrDefault();
                    if (cause is null) continue;

                    _binding.Add(handle, cause);
                    grew = true;
                }
            } while (grew);
        }

        // Rule 5's causes, which are its fields: one whose layout needs a far-side type, and, when asked, a
        // static one naming the far side at all, which rule 5 forbids though a reference-typed one does not
        // make the type bind when it loads.
        private IEnumerable<string> FieldCauses(TypeDefinitionHandle handle, bool withStaticReferences)
        {
            TypeDefinition type = Metadata.GetTypeDefinition(handle);
            foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
            {
                FieldDefinition field = Metadata.GetFieldDefinition(fieldHandle);
                Shape shape = field.DecodeSignature(_shapes, null);
                bool isStatic = (field.Attributes & FieldAttributes.Static) != 0;
                bool binds = shape.IsValueType && Binds(shape);
                bool namesCore = withStaticReferences && isStatic && shape.Core.Count > 0;
                if (!binds && !namesCore) continue;

                string kind = isStatic ? "static field" : "field";

                yield return $"{kind} {FieldName(field)} is {shape.Name}";
            }
        }

        // A base type or an interface naming the far side binds it as the type loads too. That is left to rule
        // 1 rather than reported against the type, because a state machine of a boundary iterator implements
        // an enumerable of whatever it yields and is only ever loaded inside its boundary.
        private IEnumerable<string> ShapeCauses(TypeDefinitionHandle handle)
        {
            TypeDefinition type = Metadata.GetTypeDefinition(handle);
            if (!type.BaseType.IsNil && Binds(ShapeOf(type.BaseType)))
                yield return $"base type is {ShapeOf(type.BaseType).Name}";

            foreach (InterfaceImplementationHandle implementation in type.GetInterfaceImplementations())
            {
                Shape contract = ShapeOf(Metadata.GetInterfaceImplementation(implementation).Interface);
                if (Binds(contract)) yield return $"interface {contract.Name}";
            }
        }

        private bool Binds(Shape shape)
        {
            return shape.Core.Count > 0 || shape.Own.Any(_binding.ContainsKey);
        }

        // An auto-property's backing field is read as the property it backs.
        private string FieldName(FieldDefinition field)
        {
            string name = Metadata.GetString(field.Name);
            bool backing = name.StartsWith('<') && name.EndsWith(">k__BackingField", StringComparison.Ordinal);

            return backing ? name.Substring(1, name.IndexOf('>') - 1) : name;
        }

        private SortedSet<string> NamedBy(Method method)
        {
            var named = new SortedSet<string>(StringComparer.Ordinal);
            MethodSignature<Shape> signature = method.Definition.DecodeSignature(_shapes, null);
            AddShape(named, signature.ReturnType, null);
            foreach (Shape parameter in signature.ParameterTypes) AddShape(named, parameter, null);

            if (method.Body is not { } body) return named;

            if (!body.LocalSignature.IsNil)
                foreach (Shape local in Metadata.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(_shapes, null))
                    AddShape(named, local, null);

            foreach (ExceptionRegion region in body.ExceptionRegions)
                if (!region.CatchType.IsNil)
                    AddShape(named, ShapeOf(region.CatchType), null);

            foreach (Instruction instruction in method.Instructions)
                if (instruction.CarriesToken)
                    AddEntity(named, MetadataTokens.EntityHandle(instruction.Operand));

            return named;
        }

        private void AddShape(SortedSet<string> named, Shape shape, string? through)
        {
            foreach (string core in shape.Core) named.Add(through is null ? core : $"{core} (through {through})");

            foreach (TypeDefinitionHandle own in shape.Own)
                if (_binding.TryGetValue(own, out string? cause))
                    named.Add($"{NameOf(own)} (whose {cause})");
        }

        private void AddEntity(SortedSet<string> named, EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                case HandleKind.TypeReference:
                case HandleKind.TypeSpecification:
                    AddShape(named, ShapeOf(handle), null);
                    break;
                case HandleKind.MemberReference:
                    AddMemberReference(named, (MemberReferenceHandle)handle);
                    break;
                case HandleKind.MethodDefinition:
                    MethodDefinition method = Metadata.GetMethodDefinition((MethodDefinitionHandle)handle);
                    string methodName = NameOf(method.GetDeclaringType()) + "." + Metadata.GetString(method.Name);
                    MethodSignature<Shape> signature = method.DecodeSignature(_shapes, null);
                    AddShape(named, signature.ReturnType, methodName);
                    foreach (Shape parameter in signature.ParameterTypes) AddShape(named, parameter, methodName);
                    AddShape(named, ShapeOf(method.GetDeclaringType()), null);
                    break;
                case HandleKind.FieldDefinition:
                    FieldDefinition field = Metadata.GetFieldDefinition((FieldDefinitionHandle)handle);
                    string fieldName = NameOf(field.GetDeclaringType()) + "." + FieldName(field);
                    AddShape(named, field.DecodeSignature(_shapes, null), fieldName);
                    AddShape(named, ShapeOf(field.GetDeclaringType()), null);
                    break;
                case HandleKind.MethodSpecification:
                    MethodSpecification specification = Metadata.GetMethodSpecification((MethodSpecificationHandle)handle);
                    AddEntity(named, specification.Method);
                    foreach (Shape argument in specification.DecodeSignature(_shapes, null))
                        AddShape(named, argument, null);
                    break;
                case HandleKind.StandaloneSignature:
                    MethodSignature<Shape> pointer = Metadata.GetStandaloneSignature((StandaloneSignatureHandle)handle)
                        .DecodeMethodSignature(_shapes, null);
                    AddShape(named, pointer.ReturnType, null);
                    foreach (Shape parameter in pointer.ParameterTypes) AddShape(named, parameter, null);
                    break;
            }
        }

        // A member of a far-side type is named as that member; a member of any other type names whatever
        // far-side types its owner's instantiation and its own signature mention.
        private void AddMemberReference(SortedSet<string> named, MemberReferenceHandle handle)
        {
            MemberReference member = Metadata.GetMemberReference(handle);
            string name = Metadata.GetString(member.Name);
            Shape? parent = member.Parent.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference
                or HandleKind.TypeSpecification
                ? ShapeOf(member.Parent)
                : null;

            if (parent is { IsGuarded: true })
            {
                named.Add(parent.Name + "." + name);

                return;
            }

            if (parent is not null) AddShape(named, parent, null);

            string through = (parent?.Name ?? "?") + "." + name;
            if (member.GetKind() == MemberReferenceKind.Field)
            {
                AddShape(named, member.DecodeFieldSignature(_shapes, null), through);

                return;
            }

            MethodSignature<Shape> signature = member.DecodeMethodSignature(_shapes, null);
            AddShape(named, signature.ReturnType, through);
            foreach (Shape parameter in signature.ParameterTypes) AddShape(named, parameter, through);
        }

        private void MapSites()
        {
            foreach (Method method in _methods.Values)
            foreach (Instruction instruction in method.Instructions)
            {
                if (!instruction.NamesMethod) continue;
                if (OwnMethod(MetadataTokens.EntityHandle(instruction.Operand)) is not { } target) continue;

                _methods[target].Sites.Add(method.Handle);
            }
        }

        // The members Roslyn calls on an analyzer or a provider itself: every override on a type deriving from
        // one of its own, and the public constructor it creates one with.
        private void MarkRoslynMembers()
        {
            foreach (TypeDefinitionHandle handle in Metadata.TypeDefinitions)
            {
                TypeDefinition type = Metadata.GetTypeDefinition(handle);
                if (type.BaseType.Kind != HandleKind.TypeReference) continue;

                TypeReference baseType = Metadata.GetTypeReference((TypeReferenceHandle)type.BaseType);
                if (!Metadata.GetString(baseType.Namespace).StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
                    continue;

                foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                {
                    Method method = _methods[methodHandle];
                    MethodAttributes attributes = method.Definition.Attributes;
                    bool overrides = (attributes & MethodAttributes.Virtual) != 0;
                    bool creates = method.Name == ".ctor"
                                   && (attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
                    if (!overrides && !creates) continue;

                    method.IsRoslynMember = true;
                    method.ReachedByRoslyn = true;
                }
            }
        }

        private void CollectStoredDelegates()
        {
            foreach (Method method in _methods.Values)
                if (method.Body is not null)
                    Flow(method, store: true);
        }

        // Every delegate Roslyn is handed at a call in this method: marked as reached by Roslyn, and a breach
        // where it is a declared boundary or cannot be followed.
        private void CheckReceipts(Method method, List<FirewallBreach> breaches)
        {
            List<(int Index, string Receipt)> receipts = method.Instructions
                .Select((instruction, index) => (Index: index, Receipt: instruction.NamesMethod
                    ? ReceiptName(instruction)
                    : null))
                .Where(entry => entry.Receipt is not null)
                .Select(entry => (entry.Index, entry.Receipt!))
                .ToList();
            if (receipts.Count == 0) return;

            Frame?[] frames = Flow(method, store: false);
            foreach ((int index, string receipt) in receipts)
            {
                if (frames[index] is not { } frame) continue;

                Instruction instruction = method.Instructions[index];
                MethodSignature<Shape> signature = SignatureOf(MetadataTokens.EntityHandle(instruction.Operand));
                int count = signature.ParameterTypes.Length;
                for (var parameter = 0; parameter < count; parameter++)
                {
                    string type = signature.ParameterTypes[parameter].Name;
                    if (!type.StartsWith("System.Action", StringComparison.Ordinal)
                        && !type.StartsWith("System.Func", StringComparison.Ordinal))
                        continue;

                    ImmutableHashSet<int> targets = frame.Stack[frame.Stack.Count - count + parameter];
                    if (targets.Count == 0)
                    {
                        breaches.Add(new FirewallBreach(
                            method.Display, HandsRoslyn, $"{receipt} is handed a delegate the walk cannot follow"));
                        continue;
                    }

                    foreach (int target in targets)
                    {
                        if (OwnMethod(MetadataTokens.EntityHandle(target)) is not { } own) continue;

                        Method handed = _methods[own];
                        handed.ReachedByRoslyn = true;
                        if (handed.IsDeclaredBoundary)
                            breaches.Add(new FirewallBreach(method.Display, HandsRoslyn, $"{receipt} is handed {handed.Display}"));
                    }
                }
            }
        }

        // The Roslyn member a call hands a delegate to, where it is one: an action registration on an analysis
        // context, or a value provider's constructor.
        private string? ReceiptName(Instruction instruction)
        {
            EntityHandle handle = MetadataTokens.EntityHandle(instruction.Operand);
            if (handle.Kind == HandleKind.MethodSpecification)
                handle = Metadata.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
            if (handle.Kind != HandleKind.MemberReference) return null;

            MemberReference member = Metadata.GetMemberReference((MemberReferenceHandle)handle);
            if (ParentName(member.Parent) is not { } parent) return null;
            if (parent.Namespace != "Microsoft.CodeAnalysis.Diagnostics") return null;

            string name = Metadata.GetString(member.Name);
            bool registers = name.StartsWith("Register", StringComparison.Ordinal)
                             && name.EndsWith("Action", StringComparison.Ordinal);
            bool computes = name == ".ctor" && parent.Name.EndsWith("ValueProvider`1", StringComparison.Ordinal);

            return registers || computes ? $"{parent.Name}.{name}" : null;
        }

        // Which methods each stack slot and local could hold a delegate to, at every instruction, found by
        // running the method's flow to a fixed point. Storing to a field records what was stored, for every
        // later read of that field anywhere.
        private Frame?[] Flow(Method method, bool store)
        {
            IReadOnlyList<Instruction> code = method.Instructions;
            var indexOf = new Dictionary<int, int>();
            for (var index = 0; index < code.Count; index++) indexOf[code[index].Offset] = index;

            var frames = new Frame?[code.Count];
            var work = new Stack<int>();

            void Reach(int offset, Frame frame)
            {
                if (!indexOf.TryGetValue(offset, out int index)) return;

                Frame? joined = frames[index] is { } held ? held.Joined(frame) : frame;
                if (joined is null) return;

                frames[index] = joined;
                work.Push(index);
            }

            Reach(0, Frame.Empty);
            // A catch handler and a filter start with the exception object as their one stack slot; a finally
            // or fault handler starts with an empty stack.
            var caught = new Frame([Frame.Nothing], ImmutableDictionary<int, ImmutableHashSet<int>>.Empty);
            foreach (ExceptionRegion region in method.Body!.ExceptionRegions)
            {
                bool catches = region.Kind is ExceptionRegionKind.Catch or ExceptionRegionKind.Filter;
                Reach(region.HandlerOffset, catches ? caught : Frame.Empty);
                if (region.Kind == ExceptionRegionKind.Filter) Reach(region.FilterOffset, caught);
            }

            bool returnsValue = method.Definition.DecodeSignature(_shapes, null).ReturnType.Name != "Void";
            while (work.Count > 0)
            {
                int index = work.Pop();
                Instruction instruction = code[index];
                Frame after = Step(instruction, frames[index]!, returnsValue, store);

                FlowControl flow = instruction.OpCode.FlowControl;
                if (flow is FlowControl.Return or FlowControl.Throw) continue;

                foreach (int target in instruction.Targets) Reach(target, after);
                if (flow != FlowControl.Branch) Reach(instruction.Next, after);
            }

            return frames;
        }

        private Frame Step(Instruction instruction, Frame frame, bool returnsValue, bool store)
        {
            ImmutableList<ImmutableHashSet<int>>.Builder stack = frame.Stack.ToBuilder();
            ImmutableDictionary<int, ImmutableHashSet<int>> locals = frame.Locals;

            ImmutableHashSet<int> Pop()
            {
                ImmutableHashSet<int> top = stack[^1];
                stack.RemoveAt(stack.Count - 1);

                return top;
            }

            void Discard(int count)
            {
                for (var index = 0; index < count; index++) Pop();
            }

            OpCode code = instruction.OpCode;
            if (code == OpCodes.Dup)
            {
                ImmutableHashSet<int> top = Pop();
                stack.Add(top);
                stack.Add(top);
            }
            else if (code == OpCodes.Ldftn)
            {
                stack.Add(Frame.Nothing.Add(instruction.Operand));
            }
            else if (code == OpCodes.Ldvirtftn)
            {
                Pop();
                stack.Add(Frame.Nothing.Add(instruction.Operand));
            }
            else if (code == OpCodes.Newobj)
            {
                MethodSignature<Shape> signature = SignatureOf(MetadataTokens.EntityHandle(instruction.Operand));
                int count = signature.ParameterTypes.Length;
                bool isDelegate = count == 2 && signature.ParameterTypes[0].Name == "Object"
                                             && signature.ParameterTypes[1].Name == "IntPtr";
                ImmutableHashSet<int> function = isDelegate ? stack[^1] : Frame.Nothing;
                Discard(count);
                stack.Add(function);
            }
            else if (code == OpCodes.Call || code == OpCodes.Callvirt)
            {
                MethodSignature<Shape> signature = SignatureOf(MetadataTokens.EntityHandle(instruction.Operand));
                Discard(signature.ParameterTypes.Length + (signature.Header.IsInstance ? 1 : 0));
                if (signature.ReturnType.Name != "Void") stack.Add(Frame.Nothing);
            }
            else if (code == OpCodes.Calli)
            {
                MethodSignature<Shape> signature = Metadata
                    .GetStandaloneSignature((StandaloneSignatureHandle)MetadataTokens.EntityHandle(instruction.Operand))
                    .DecodeMethodSignature(_shapes, null);
                Discard(signature.ParameterTypes.Length + (signature.Header.IsInstance ? 1 : 0) + 1);
                if (signature.ReturnType.Name != "Void") stack.Add(Frame.Nothing);
            }
            else if (code == OpCodes.Ret)
            {
                if (returnsValue) Pop();
            }
            else if (code == OpCodes.Leave || code == OpCodes.Leave_S)
            {
                stack.Clear();
            }
            else if (LocalStored(instruction) is { } stored)
            {
                locals = locals.SetItem(stored, Pop());
            }
            else if (LocalLoaded(instruction) is { } loaded)
            {
                stack.Add(locals.GetValueOrDefault(loaded, Frame.Nothing));
            }
            else if (code == OpCodes.Stsfld || code == OpCodes.Stfld)
            {
                ImmutableHashSet<int> value = Pop();
                if (code == OpCodes.Stfld) Pop();
                if (store && value.Count > 0)
                    _stored[instruction.Operand] = _stored.GetValueOrDefault(instruction.Operand, Frame.Nothing).Union(value);
            }
            else if (code == OpCodes.Ldsfld || code == OpCodes.Ldfld)
            {
                if (code == OpCodes.Ldfld) Pop();
                stack.Add(_stored.GetValueOrDefault(instruction.Operand, Frame.Nothing));
            }
            else
            {
                Discard(Pops(code));
                for (var index = 0; index < Pushes(code); index++) stack.Add(Frame.Nothing);
            }

            return new Frame(stack.ToImmutable(), locals);
        }

        private static int? LocalStored(Instruction instruction)
        {
            OpCode code = instruction.OpCode;
            if (code == OpCodes.Stloc_0) return 0;
            if (code == OpCodes.Stloc_1) return 1;
            if (code == OpCodes.Stloc_2) return 2;
            if (code == OpCodes.Stloc_3) return 3;
            if (code == OpCodes.Stloc_S || code == OpCodes.Stloc) return instruction.Operand;

            return null;
        }

        private static int? LocalLoaded(Instruction instruction)
        {
            OpCode code = instruction.OpCode;
            if (code == OpCodes.Ldloc_0) return 0;
            if (code == OpCodes.Ldloc_1) return 1;
            if (code == OpCodes.Ldloc_2) return 2;
            if (code == OpCodes.Ldloc_3) return 3;
            if (code == OpCodes.Ldloc_S || code == OpCodes.Ldloc) return instruction.Operand;

            return null;
        }

        private static int Pops(OpCode code)
        {
            return code.StackBehaviourPop switch
            {
                StackBehaviour.Pop0 => 0,
                StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
                StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi
                    or StackBehaviour.Popi_popi8 or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8
                    or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
                StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8
                    or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8
                    or StackBehaviour.Popref_popi_popref or StackBehaviour.Popref_popi_pop1 => 3,
                _ => throw new InvalidOperationException($"The walk has no stack count for {code.Name}.")
            };
        }

        private static int Pushes(OpCode code)
        {
            return code.StackBehaviourPush switch
            {
                StackBehaviour.Push0 => 0,
                StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4
                    or StackBehaviour.Pushr8 or StackBehaviour.Pushref => 1,
                StackBehaviour.Push1_push1 => 2,
                _ => throw new InvalidOperationException($"The walk has no stack count for {code.Name}.")
            };
        }

        // Declared boundaries are boundaries; a state machine's methods are whatever its kickoff is; a closure
        // is one while every method that reaches it is one and Roslyn is never handed it. Everything else is
        // not. The closures start in and are struck out until nothing changes, so a local function that calls
        // itself inside a boundary stays in.
        private void DecideBoundaries()
        {
            foreach (Method method in _methods.Values)
                method.IsBoundary = method.IsDeclaredBoundary
                                    || KickoffOf(method) is not null
                                    || (method.IsClosure && !method.ReachedByRoslyn);

            bool changed;
            do
            {
                changed = false;
                foreach (Method method in _methods.Values)
                {
                    if (!method.IsBoundary || method.IsDeclaredBoundary) continue;

                    bool holds = KickoffOf(method) is { } kickoff
                        ? _methods[kickoff].IsBoundary
                        : method.Sites.Count > 0 && method.Sites.All(site => _methods[site].IsBoundary);
                    if (holds) continue;

                    method.IsBoundary = false;
                    changed = true;
                }
            } while (changed);
        }

        private List<string> UnguardedBoundaryCalls(Method method)
        {
            var calls = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Instruction instruction in method.Instructions)
            {
                if (!instruction.NamesMethod) continue;
                if (OwnMethod(MetadataTokens.EntityHandle(instruction.Operand)) is not { } target) continue;
                if (!_methods[target].IsBoundary) continue;
                if (IsGuarded(method, instruction.Offset)) continue;

                calls.Add(_methods[target].Display);
            }

            return calls.ToList();
        }

        private bool IsGuarded(Method method, int offset)
        {
            foreach (ExceptionRegion region in method.Body!.ExceptionRegions)
            {
                if (offset < region.TryOffset || offset >= region.TryOffset + region.TryLength) continue;
                if (IsGuard(method, region)) return true;
            }

            return false;
        }

        // A catch clause of System.Exception, or a filter whose block tests for it, which is how C# writes a
        // `catch (Exception e) when (...)`. A finally or a fault clause guards nothing.
        private bool IsGuard(Method method, ExceptionRegion region)
        {
            return region.Kind switch
            {
                ExceptionRegionKind.Catch => IsSystemException(region.CatchType),
                ExceptionRegionKind.Filter => method.Instructions.Any(instruction =>
                    instruction.Offset >= region.FilterOffset && instruction.Offset < region.HandlerOffset
                                                              && instruction.OpCode == OpCodes.Isinst
                                                              && IsSystemException(MetadataTokens.EntityHandle(instruction.Operand))),
                _ => false
            };
        }

        private bool IsSystemException(EntityHandle handle)
        {
            return handle.Kind == HandleKind.TypeReference && NameOf((TypeReferenceHandle)handle) == "System.Exception";
        }

        // Every method the fault path's own methods and the static constructors call, however deep, must
        // name nothing on the far side. A delegate a static constructor creates is not followed: creating one
        // runs nothing.
        private void CheckFaultPath(IReadOnlyList<string> faultPath, List<FirewallBreach> breaches)
        {
            var reached = new Dictionary<MethodDefinitionHandle, string>();
            var work = new Queue<MethodDefinitionHandle>();
            foreach (string root in faultPath)
            {
                List<Method> matches = _methods.Values.Where(method => method.Display == root)
                    .ToList();
                if (matches.Count == 0)
                    breaches.Add(new FirewallBreach(root, FaultPathNamesCore, "no such method"));

                foreach (Method match in matches)
                    if (reached.TryAdd(match.Handle, root))
                        work.Enqueue(match.Handle);
            }

            foreach (Method initializer in _methods.Values.Where(method => method.Name == ".cctor"))
                if (reached.TryAdd(initializer.Handle, initializer.Display))
                    work.Enqueue(initializer.Handle);

            while (work.Count > 0)
            {
                Method method = _methods[work.Dequeue()];
                foreach (Instruction instruction in method.Instructions)
                {
                    bool calls = instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt
                                                                    || instruction.OpCode == OpCodes.Newobj;
                    if (!calls) continue;
                    if (OwnMethod(MetadataTokens.EntityHandle(instruction.Operand)) is not { } target) continue;
                    if (reached.TryAdd(target, reached[method.Handle])) work.Enqueue(target);
                }
            }

            foreach ((MethodDefinitionHandle handle, string root) in reached)
            {
                Method method = _methods[handle];
                if (method.Named.Count == 0) continue;

                string from = root == method.Display ? string.Empty : $" (reached from {root})";
                breaches.Add(new FirewallBreach(method.Display, FaultPathNamesCore, string.Join(", ", method.Named) + from));
            }
        }

        private void CheckTypeLoads(List<FirewallBreach> breaches)
        {
            foreach (TypeDefinitionHandle handle in Metadata.TypeDefinitions)
            {
                List<string> causes = FieldCauses(handle, true)
                    .ToList();
                if (causes.Count > 0)
                    breaches.Add(new FirewallBreach(NameOf(handle), LoadBindsCore, string.Join(", ", causes)));
            }
        }
    }
}
