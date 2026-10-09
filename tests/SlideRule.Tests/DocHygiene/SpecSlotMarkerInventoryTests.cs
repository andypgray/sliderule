using System.Reflection;
using System.Runtime.CompilerServices;
using Shouldly;
using Xunit;
using SlideRule.Validation;

namespace SlideRule.Tests.DocHygiene;

/// <summary>
///     The inventory that holds every string parameter of the authoring surface to carrying a slot
///     marker, and every marker to naming the slot that parameter really is.
/// </summary>
/// <remarks>
///     <para>
///         <b>What goes wrong without it.</b> The marker is what lets a reader outside this assembly say
///         which slot a literal fills, and nothing else reports its absence: a verb shipped without one
///         compiles, checks and renders exactly as it should, and the only symptom is a reader that stays
///         silent about that verb forever. A wrong name is worse than a missing one, because the reader
///         then speaks about the value under the wrong noun.
///     </para>
///     <para>
///         <b>What to do on a red.</b> A parameter reported as unaccounted needs
///         <c>[SpecSlot(nameof(SpecSlot.Something))]</c> at its declaration, on whichever declaration a
///         chained call binds to — for a stage clause that is the interface member, not the builder
///         class that implements it. A parameter that is genuinely not a slot goes in
///         <see cref="NotSlots" /> with the sentence saying why. A slot name the catalog cannot resolve
///         means the marker and <see cref="SpecSlot" /> have drifted; add the slot there rather than
///         inventing a second vocabulary here. A row reported against <see cref="Slots" /> is the table
///         and the surface disagreeing about which noun a value carries, so decide which is right and
///         move the one that is wrong.
///     </para>
///     <para>
///         <b>Why the walk is scoped by namespace.</b> The slots live in the root namespace and in
///         <c>Fluent</c>, and nowhere else in this assembly. Widened past those two the walk sweeps in
///         every rendering, checking and read-model member that happens to take a string, and the
///         registry below turns into a list of dozens that churns on unrelated work. Caller-info
///         parameters are left out mechanically, because the compiler supplies them and no author ever
///         writes a literal at one.
///     </para>
/// </remarks>
public sealed class SpecSlotMarkerInventoryTests
{
    // The number of string literals a spec can write, counted once so that adding a verb is a deliberate
    // edit here rather than a table that grew while nobody was counting.
    private const int SlotCount = 74;

    private const string RootNamespace = "SlideRule";

    private const string FluentNamespace = RootNamespace + ".Fluent";

    private const string MatcherUtility =
        "A matcher utility rather than authoring vocabulary: a spec never writes this call, and the "
        + "pattern reaching it has already been through the slot that accepted it.";

    /// <summary>
    ///     The string parameters inside the walk's reach that are deliberately not slots, each with the
    ///     sentence saying why.
    /// </summary>
    /// <remarks>
    ///     Two-sided: an entry naming a parameter that no longer exists is dead, an entry naming a
    ///     parameter that carries a marker is stale, and each has its own arm. The keys are the same
    ///     spelling <see cref="Slots" /> uses, so both tables read alike.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> NotSlots =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Fluent.NamespacePattern..ctor(string)/pattern"] = MatcherUtility,
            ["Fluent.NamespacePattern.Validate(string)/pattern"] =
                "The owner of the namespace-glob verdict itself. Marking it would have the slot catalog "
                + "point at the method its own decision is made by.",
            ["Fluent.NamespacePattern.Matches(string)/namespace"] =
                "A namespace read out of the solution, tested against a pattern. It is the candidate, "
                + "never the literal an author wrote.",
            ["Fluent.TypeNamePattern..ctor(string)/pattern"] = MatcherUtility,
            ["Fluent.TypeNamePattern.Matches(string)/name"] =
                "A type name read out of the solution, tested against a pattern, on the same footing as "
                + "the namespace matcher above."
        };

    /// <summary>
    ///     Every marked parameter with the slot it fills: the whole authoring surface, one row per
    ///     literal an author can write.
    /// </summary>
    /// <remarks>
    ///     A parameter is keyed by its declaring type, its member, the member's parameter list and its own
    ///     name, so two overloads of one verb stay apart and a signature change is visible here rather
    ///     than silently rebinding a row. The values are the catalog's own constants, so a renamed slot
    ///     fails the build rather than this gate.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Slots =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Arch.Layer(string, string, string[])/name"] = nameof(SpecSlot.LayerName),
            ["Arch.Layer(string, string, string[])/glob"] = nameof(SpecSlot.NamespacePattern),
            ["Arch.Layer(string, string, string[])/more"] = nameof(SpecSlot.NamespacePattern),
            ["Arch.Layer(string, Selection)/name"] = nameof(SpecSlot.LayerName),
            ["Arch.Namespace(string)/glob"] = nameof(SpecSlot.NamespacePattern),
            ["Arch.Project(string)/name"] = nameof(SpecSlot.ProjectName),
            ["Arch.Member(Type, string, string, int)/name"] = nameof(SpecSlot.MemberName),
            ["Arch.Rule(string, string, int)/id"] = nameof(SpecSlot.RuleId),
            ["Arch.Scope(string, string, int)/id"] = nameof(SpecSlot.ScopeId),

            ["Layer.Purpose(string)/prose"] = nameof(SpecSlot.Purpose),

            ["SelectionAdjectives.InNamespace(Selection, string)/glob"] = nameof(SpecSlot.NamespacePattern),
            ["SelectionAdjectives.WithSuffix(Selection, string)/suffix"] = nameof(SpecSlot.Suffix),
            ["SelectionAdjectives.WithPrefix(Selection, string)/prefix"] = nameof(SpecSlot.Prefix),
            ["SelectionAdjectives.WithNameMatching(Selection, string)/glob"] = nameof(SpecSlot.NamePattern),
            ["SelectionAdjectives.Named(Selection, string, string[])/first"] = nameof(SpecSlot.ExactTypeName),
            ["SelectionAdjectives.Named(Selection, string, string[])/more"] = nameof(SpecSlot.ExactTypeName),
            ["SelectionAdjectives.Implementing(Selection, string)/interfaceFullName"] = nameof(SpecSlot.InterfaceName),
            ["SelectionAdjectives.DerivedFrom(Selection, string)/baseTypeFullName"] = nameof(SpecSlot.BaseTypeName),
            ["SelectionAdjectives.AttributedWith(Selection, string)/attributeFullName"] = nameof(SpecSlot.AttributeName),
            ["SelectionAdjectives.Where(Selection, Func<ITypeInfo, bool>, string)/description"] = nameof(SpecSlot.Description),

            ["SelectionConstraints.MustHaveExactlyOneCounterpart(Selection, Selection, string)/named"] = nameof(SpecSlot.CounterpartTemplate),
            ["SelectionConstraints.MustResideInNamespace(Selection, string)/glob"] = nameof(SpecSlot.NamespacePattern),
            ["SelectionConstraints.MustResideInProject(Selection, string)/projectName"] = nameof(SpecSlot.ProjectName),
            ["SelectionConstraints.MustHaveSuffix(Selection, string)/suffix"] = nameof(SpecSlot.Suffix),
            ["SelectionConstraints.MustHavePrefix(Selection, string)/prefix"] = nameof(SpecSlot.Prefix),
            ["SelectionConstraints.MustHaveNameMatching(Selection, string)/glob"] = nameof(SpecSlot.NamePattern),
            ["SelectionConstraints.MustImplement(Selection, string)/interfaceFullName"] = nameof(SpecSlot.InterfaceName),
            ["SelectionConstraints.MustDeriveFrom(Selection, string)/baseTypeFullName"] = nameof(SpecSlot.BaseTypeName),
            ["SelectionConstraints.MustBeAttributedWith(Selection, string)/attributeFullName"] = nameof(SpecSlot.AttributeName),
            ["SelectionConstraints.MustNotImplement(Selection, string, string[])/first"] = nameof(SpecSlot.InterfaceName),
            ["SelectionConstraints.MustNotImplement(Selection, string, string[])/more"] = nameof(SpecSlot.InterfaceName),
            ["SelectionConstraints.MustNotDeriveFrom(Selection, string, string[])/first"] = nameof(SpecSlot.BaseTypeName),
            ["SelectionConstraints.MustNotDeriveFrom(Selection, string, string[])/more"] = nameof(SpecSlot.BaseTypeName),
            ["SelectionConstraints.MustNotBeAttributedWith(Selection, string, string[])/first"] = nameof(SpecSlot.AttributeName),
            ["SelectionConstraints.MustNotBeAttributedWith(Selection, string, string[])/more"] = nameof(SpecSlot.AttributeName),
            ["SelectionConstraints.Must(Selection, Func<ITypeInfo, bool>, string)/description"] = nameof(SpecSlot.Description),

            ["MemberSelectionAdjectives.WithSuffix(TSelf, string)/suffix"] = nameof(SpecSlot.MemberSuffix),
            ["MemberSelectionAdjectives.WithPrefix(TSelf, string)/prefix"] = nameof(SpecSlot.MemberPrefix),
            ["MemberSelectionAdjectives.WithNameMatching(TSelf, string)/glob"] = nameof(SpecSlot.MemberNamePattern),
            ["MemberSelectionAdjectives.AttributedWith(TSelf, string)/attributeFullName"] = nameof(SpecSlot.AttributeName),
            ["MemberSelectionAdjectives.Where(TSelf, Func<IMemberInfo, bool>, string)/description"] = nameof(SpecSlot.Description),

            ["MemberSelectionConstraints.MustHaveSuffix(MemberSelection, string)/suffix"] = nameof(SpecSlot.MemberSuffix),
            ["MemberSelectionConstraints.MustHavePrefix(MemberSelection, string)/prefix"] = nameof(SpecSlot.MemberPrefix),
            ["MemberSelectionConstraints.MustHaveNameMatching(MemberSelection, string)/glob"] = nameof(SpecSlot.MemberNamePattern),
            ["MemberSelectionConstraints.MustBeAttributedWith(MemberSelection, string)/attributeFullName"] = nameof(SpecSlot.AttributeName),
            ["MemberSelectionConstraints.MustNotBeAttributedWith(MemberSelection, string, string[])/first"] = nameof(SpecSlot.AttributeName),
            ["MemberSelectionConstraints.MustNotBeAttributedWith(MemberSelection, string, string[])/more"] = nameof(SpecSlot.AttributeName),
            ["MemberSelectionConstraints.Must(MemberSelection, Func<IMemberInfo, bool>, string)/description"] = nameof(SpecSlot.Description),

            ["MethodSelectionConstraints.MustAcceptParameter(MethodSelection, string)/parameterTypeFullName"] = nameof(SpecSlot.ParameterTypeName),

            ["Fluent.MethodSelection.Returning(string, string[])/first"] = nameof(SpecSlot.ReturnTypeName),
            ["Fluent.MethodSelection.Returning(string, string[])/more"] = nameof(SpecSlot.ReturnTypeName),

            ["ProjectSelectionAdjectives.Named(ProjectSelection, string, string[])/first"] = nameof(SpecSlot.ProjectNameOperand),
            ["ProjectSelectionAdjectives.Named(ProjectSelection, string, string[])/more"] = nameof(SpecSlot.ProjectNameOperand),
            ["ProjectSelectionAdjectives.Matching(ProjectSelection, string, string[])/glob"] = nameof(SpecSlot.ProjectNamePattern),
            ["ProjectSelectionAdjectives.Matching(ProjectSelection, string, string[])/more"] = nameof(SpecSlot.ProjectNamePattern),
            ["ProjectSelectionAdjectives.Where(ProjectSelection, Func<IProjectInfo, bool>, string)/description"] = nameof(SpecSlot.Description),

            ["ProjectSelectionConstraints.MustOnlyTarget(ProjectSelection, string, string[])/first"] = nameof(SpecSlot.TargetFramework),
            ["ProjectSelectionConstraints.MustOnlyTarget(ProjectSelection, string, string[])/more"] = nameof(SpecSlot.TargetFramework),
            ["ProjectSelectionConstraints.Must(ProjectSelection, Func<IProjectInfo, bool>, string)/description"] = nameof(SpecSlot.Description),

            ["IRuleBuilder.Migrate(string, Constraint)/from"] = nameof(SpecSlot.MigrateFrom),
            ["Fluent.IEnforceRule.Because(string)/because"] = nameof(SpecSlot.Because),
            ["Fluent.IEnforceRule.Fix(string)/fix"] = nameof(SpecSlot.Fix),
            ["Fluent.IEnforceRule.Citation(string)/uri"] = nameof(SpecSlot.Citation),
            ["Fluent.IMigrateRule.Because(string)/because"] = nameof(SpecSlot.Because),
            ["Fluent.IMigrateRule.Fix(string)/fix"] = nameof(SpecSlot.Fix),
            ["Fluent.IMigrateRule.Citation(string)/uri"] = nameof(SpecSlot.Citation),
            ["Fluent.IMigrateRule.Baseline(string)/path"] = nameof(SpecSlot.Baseline),
            ["Fluent.ICautionedScope.Dragons(string)/prose"] = nameof(SpecSlot.Dragons),
            ["Fluent.ICautionedScope.DragonsDoc(string)/path"] = nameof(SpecSlot.DragonsDoc),
            ["Fluent.ICautionedScope.Because(string)/because"] = nameof(SpecSlot.Because),
            ["Fluent.IQuarantinedScope.Dragons(string)/prose"] = nameof(SpecSlot.Dragons),
            ["Fluent.IQuarantinedScope.DragonsDoc(string)/path"] = nameof(SpecSlot.DragonsDoc),
            ["Fluent.IQuarantinedScope.Baseline(string)/path"] = nameof(SpecSlot.Baseline),
            ["Fluent.IQuarantinedScope.Because(string)/because"] = nameof(SpecSlot.Because)
        };

    [Fact]
    public void EveryStringParameter_IsMarked_OrRegisteredAsSomethingElse()
    {
        // Arrange
        IReadOnlyDictionary<string, string> marked = MarkedParameters();

        // Act
        List<string> unaccounted = Universe()
            .Select(Key)
            .Where(key => !marked.ContainsKey(key) && !NotSlots.ContainsKey(key))
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        unaccounted.ShouldReportNothing(
            "String parameter(s) of the authoring surface carry neither a slot marker nor a registry "
            + "entry, so a literal written at one names a slot no reader outside this assembly can see");
    }

    [Fact]
    public void NoRegistryEntry_NamesAParameterThatIsGone()
    {
        // Arrange
        HashSet<string> present = Universe()
            .Select(Key)
            .ToHashSet(StringComparer.Ordinal);

        // Act
        List<string> dead = NotSlots.Keys
            .Where(key => !present.Contains(key))
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        dead.ShouldReportNothing(
            "Registry entr(ies) name a string parameter the authoring surface no longer declares; delete "
            + "the entry, or correct it if the declaration only changed shape");
    }

    [Fact]
    public void NoRegistryEntry_NamesAParameterThatIsMarked()
    {
        // Arrange
        IReadOnlyDictionary<string, string> marked = MarkedParameters();

        // Act
        List<string> stale = NotSlots.Keys
            .Where(marked.ContainsKey)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        stale.ShouldReportNothing(
            "Registry entr(ies) say a parameter is not a slot while its declaration carries a marker; the "
            + "marker decides, so the entry comes out");
    }

    [Fact]
    public void EveryMarker_NamesASlotTheCatalogDeclares()
    {
        // Act
        List<string> unknown = MarkedParameters()
            .Where(entry => SpecSlot.ByName(entry.Value) is null)
            .Select(entry => Row(entry.Key, entry.Value))
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        unknown.ShouldReportNothing(
            "Marker(s) name a slot SpecSlot does not declare, so nothing can resolve the noun or the "
            + "verdict the value draws");
    }

    [Fact]
    public void TheAuthoringSurface_CarriesOneMarkerPerStringSlot()
    {
        // Act
        int marked = MarkedParameters().Count;

        // Assert: the count stands on its own rather than reading the table's length, so a row added to
        // one side alone cannot make both sides agree about a surface that changed.
        marked.ShouldBe(SlotCount,
            $"The authoring surface carries {marked} marked string parameters against the {SlotCount} it "
            + "is known to have. A new verb brings its rows and this number with it.");
        Slots.Count.ShouldBe(SlotCount,
            $"The slot table holds {Slots.Count} rows against the {SlotCount} slots the surface has.");
    }

    [Fact]
    public void EveryMarkedParameter_CarriesTheSlotTheSurfaceMeans()
    {
        // Arrange
        List<string> expected = Slots
            .Select(entry => Row(entry.Key, entry.Value))
            .ToList();
        List<string> actual = MarkedParameters()
            .Select(entry => Row(entry.Key, entry.Value))
            .ToList();

        // Act
        List<string> tabled = expected
            .Except(actual, StringComparer.Ordinal)
            .Select(row => "in the table, not on the surface: " + row)
            .ToList();
        List<string> surfaced = actual
            .Except(expected, StringComparer.Ordinal)
            .Select(row => "on the surface, not in the table: " + row)
            .ToList();
        List<string> drift = tabled
            .Concat(surfaced)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert
        drift.ShouldReportNothing(
            "The slot table and the markers disagree. A wrong slot makes a reader name the value by the "
            + "wrong noun and report it under the wrong code, so decide which side is right before moving "
            + "either");
    }

    // Keyed rather than listed, so every arm can ask about one parameter without walking the surface again.
    // Internal because the completion provider's agreement gate is held over the same inventory: the two
    // readers of the marker have to agree about every parameter there is, and two walks of the surface
    // could agree about different sets of it.
    internal static IReadOnlyDictionary<string, string> MarkedParameters()
    {
        var marked = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ParameterInfo parameter in Universe())
        {
            var marker = parameter.GetCustomAttribute<SpecSlotAttribute>();
            if (marker is null) continue;

            marked[Key(parameter)] = marker.Name;
        }

        return marked;
    }

    private static IEnumerable<ParameterInfo> Universe()
    {
        return typeof(Arch).Assembly.GetExportedTypes()
            .Where(IsAuthoringSurface)
            .SelectMany(PublicMembers)
            .SelectMany(member => member.GetParameters())
            .Where(IsSlotCandidate);
    }

    private static bool IsAuthoringSurface(Type type)
    {
        return type.Namespace == RootNamespace || type.Namespace == FluentNamespace;
    }

    // Declared members only: an inherited method answers on the type that declares it, and counting it
    // twice would put one parameter under two keys.
    private static IEnumerable<MethodBase> PublicMembers(Type type)
    {
        MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        ConstructorInfo[] constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        IEnumerable<MethodBase> members = methods.Concat<MethodBase>(constructors);

        return members.Where(member => member.DeclaringType == type);
    }

    private static bool IsSlotCandidate(ParameterInfo parameter)
    {
        bool takesText = parameter.ParameterType == typeof(string) || parameter.ParameterType == typeof(string[]);
        if (!takesText) return false;

        bool fromCaller = parameter.IsDefined(typeof(CallerFilePathAttribute), inherit: false)
                          || parameter.IsDefined(typeof(CallerMemberNameAttribute), inherit: false);

        return !fromCaller;
    }

    private static string Key(ParameterInfo parameter)
    {
        var member = (MethodBase)parameter.Member;
        string declaring = SurfaceName(member.DeclaringType!);
        string name = member is ConstructorInfo ? ".ctor" : member.Name;
        IEnumerable<string> types = member.GetParameters().Select(each => TypeName(each.ParameterType));
        string signature = string.Join(", ", types);

        return $"{declaring}.{name}({signature})/{parameter.Name}";
    }

    private static string Row(string parameter, string slot)
    {
        return $"{parameter} -> {slot}";
    }

    private static string SurfaceName(Type type)
    {
        string full = type.FullName ?? type.Name;
        string prefix = RootNamespace + ".";

        return full.StartsWith(prefix, StringComparison.Ordinal) ? full[prefix.Length..] : full;
    }

    private static string TypeName(Type type)
    {
        if (type == typeof(string)) return "string";
        if (type == typeof(object)) return "object";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(int)) return "int";
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[]";
        if (!type.IsGenericType) return type.Name;

        IEnumerable<string> arguments = type.GetGenericArguments().Select(TypeName);
        int arity = type.Name.IndexOf('`', StringComparison.Ordinal);
        string bare = arity < 0 ? type.Name : type.Name[..arity];

        return $"{bare}<{string.Join(", ", arguments)}>";
    }
}
