using System.Reflection;
using SlideRule.Cli.Mcp.Infrastructure;
using SlideRule.Roslyn;

namespace SlideRule.Cli.SpecLoading;

/// <summary>
///     Maps "the spec uses contract API this host does not have, or keeps internal" into an actionable
///     <see cref="UserErrorException" /> naming what the runtime reported, both contract versions where they
///     differ, and the remedies.
/// </summary>
/// <remarks>
///     <para>
///         This is the residual failure of the version-agnostic contract bind (see
///         <see cref="SpecLoadContext.Load" />): the spec binds to this host's copy whatever version it was
///         built against, and fails only where it reaches for something that copy does not define or, for a
///         spec built against an older contract, keeps internal. A member missing from a present type is a
///         <see cref="MissingMemberException" /> out of <c>Define()</c>. A missing type is a
///         <see cref="TypeLoadException" /> out of <c>Define()</c>. A missing type that one of the spec's own
///         types derives from or implements fails while the spec's types are discovered, as a
///         <see cref="TypeLoadException" /> inside a <see cref="ReflectionTypeLoadException" />. A type or
///         member this copy keeps internal is an access failure (<see cref="MapInaccessible" />). Any of them
///         but the discovery failure can also be raised while a spec class is created, and then arrives
///         wrapped by the reflection that created the class.
///     </para>
///     <para>
///         A <see cref="MissingMemberException" /> is also what a call into a rule pack or any other dependency
///         raises when the loaded copy lacks the member, so only a member whose declaring type this host's
///         contract defines is taken for skew (<see cref="IsContractMember" />); the namespace alone would not
///         do, because a pack sits under the contract's root namespace in an assembly of its own. A
///         <see cref="TypeLoadException" /> is also what a <c>typeof()</c> anchor on a .NET Framework-only type
///         raises, so only a type named under the contract's root namespace is taken for skew
///         (<see cref="IsMissingContractType" />). Both host surfaces render
///         message-only, so the missing name is folded into the text rather than left on the inner exception
///         where nobody sees it.
///     </para>
/// </remarks>
internal static class SpecContractMismatch
{
    // What Assembly.GetType parses as type-name grammar rather than a name. A trailing & or * resolves to a byref
    // or pointer form of a type the contract does define, so these are refused before the lookup, not after.
    private static readonly char[] TypeNameSyntax = [',', '[', ']', '&', '*', '+', '`', '<', '\\'];

    // The selection types that moved from SlideRule.Fluent to the root namespace after 0.1.2. A spec built
    // before the move names them under the old namespace, and the runtime reports that as a missing type or
    // a missing member exactly as it reports one from a newer contract, so the name is the only thing that
    // tells the two skews apart.
    private static readonly string[] MovedSelectionTypes =
    [
        "SlideRule.Fluent.MemberSelection",
        "SlideRule.Fluent.MethodSelection",
        "SlideRule.Fluent.FieldSelection",
        "SlideRule.Fluent.PropertySelection",
        "SlideRule.Fluent.ProjectSelection"
    ];

    /// <summary>
    ///     Renders the message for a spec built against a newer contract, keeping <paramref name="inner" /> as the
    ///     inner exception. <paramref name="spec" /> supplies the contract version it was compiled against.
    /// </summary>
    internal static UserErrorException Map(Assembly spec, string specDllPath, Exception inner)
    {
        string name = Path.GetFileNameWithoutExtension(specDllPath);
        string detail = string.IsNullOrWhiteSpace(inner.Message) ? "  (the runtime did not name what is missing)" : "  " + inner.Message;
        string message =
            $"The spec assembly '{name}' uses SlideRule API this tool's contract does not have{DescribeDelta(spec)}:\n"
            + detail
            + "\nThat is a spec built against a newer SlideRule package than the tool running it."
            + $"\nUpdate the tool to at least the spec's version (dotnet tool update -g SlideRule.Cli — this one is {ServerVersion.SemVer}), or reference the SlideRule package that matches this tool from the spec project and rebuild it.";
        return new UserErrorException(message, inner);
    }

    /// <summary>
    ///     Renders the message for the opposite skew: a spec that reached for contract API this host keeps internal,
    ///     which is a spec built against an older contract, from before the engine behind the authoring surface
    ///     became internal. Keeps <paramref name="inner" /> as the inner exception.
    /// </summary>
    // The runtime reports it as an access failure rather than a missing name, because the type or member is still
    // there: TypeAccessException for a type, MethodAccessException or FieldAccessException for a member.
    internal static UserErrorException MapInaccessible(Assembly spec, string specDllPath, Exception inner)
    {
        string name = Path.GetFileNameWithoutExtension(specDllPath);
        string detail = string.IsNullOrWhiteSpace(inner.Message) ? "  (the runtime did not name what it refused)" : "  " + inner.Message;
        string message =
            $"The spec assembly '{name}' uses SlideRule API that is no longer public{DescribeDelta(spec)}:\n"
            + detail
            + "\nThat is a spec built against an older SlideRule package, whose engine types were public; a spec "
            + "now reaches the engine only through the types in the SlideRule namespace."
            + "\nRewrite what it names with those types, reference the SlideRule package that matches this tool "
            + $"({ServerVersion.SemVer}) from the spec project, and rebuild it.";
        return new UserErrorException(message, inner);
    }

    /// <summary>
    ///     Renders the message for a spec built before the selection types moved out of <c>SlideRule.Fluent</c>,
    ///     whose missing name is one of those types under its old namespace. Keeps <paramref name="inner" /> as
    ///     the inner exception.
    /// </summary>
    internal static UserErrorException MapMoved(Assembly spec, string specDllPath, Exception inner)
    {
        string name = Path.GetFileNameWithoutExtension(specDllPath);
        string message =
            $"The spec assembly '{name}' uses SlideRule types under a namespace this tool's contract moved them out of{DescribeDelta(spec)}:\n"
            + "  " + inner.Message
            + "\nThat is a spec built against an older SlideRule package, where MemberSelection, MethodSelection, "
            + "FieldSelection, PropertySelection and ProjectSelection were in the SlideRule.Fluent namespace; they "
            + "are now in SlideRule."
            + $"\nReference the SlideRule package that matches this tool ({ServerVersion.SemVer}) from the spec "
            + "project and rebuild it.";
        return new UserErrorException(message, inner);
    }

    /// <summary>
    ///     True when <paramref name="typeName" /> sits under the contract's root namespace and this host's
    ///     copy of the contract does not define it: the type-level form of a spec built against a newer
    ///     contract.
    /// </summary>
    /// <remarks>
    ///     It takes the name rather than the exception because only the runtime can set
    ///     <see cref="TypeLoadException.TypeName" />. The whole root counts, not only the namespaces this copy
    ///     declares, because a newer contract may add a namespace this copy has never seen.
    /// </remarks>
    internal static bool IsMissingContractType(string typeName)
    {
        return IsUnderContractRoot(typeName) && !ContractDefines(typeName);
    }

    /// <summary>
    ///     The failure inside <paramref name="exception" /> that is contract skew, or <see langword="null" /> when
    ///     it is not: the one statement of which failures count.
    /// </summary>
    /// <remarks>
    ///     It runs inside exception filters, where anything it throws is swallowed as "no match", so it stays
    ///     pure: type tests, <see cref="IsMissingContractType" /> and <see cref="IsContractMember" /> only, and the
    ///     message is rendered by <see cref="MapSkew" /> in the catch body.
    /// </remarks>
    internal static Exception? SkewCause(Exception exception)
    {
        Exception cause = exception;
        while (cause is TargetInvocationException or TypeInitializationException && cause.InnerException is not null)
            cause = cause.InnerException;

        return cause switch
        {
            // An access failure names a type this copy still has, so the type-load arm's missing-type test would
            // pass it over.
            TypeAccessException or MethodAccessException or FieldAccessException => cause,
            MissingMemberException missing when IsContractMember(missing.Message) => cause,
            TypeLoadException typeLoad when IsMissingContractType(typeLoad.TypeName) => cause,
            ReflectionTypeLoadException typesLoad => MissingContractType(typesLoad.LoaderExceptions),
            _ => null
        };
    }

    /// <summary>
    ///     The refusal for <paramref name="cause" />, a failure <see cref="SkewCause" /> returned: the older-contract
    ///     message for an access failure or for a missing name that is a moved selection type, the newer-contract
    ///     message for anything else.
    /// </summary>
    internal static UserErrorException MapSkew(Assembly spec, string specDllPath, Exception cause)
    {
        if (cause is TypeAccessException or MethodAccessException or FieldAccessException)
            return MapInaccessible(spec, specDllPath, cause);

        return NamesMovedSelectionType(cause) ? MapMoved(spec, specDllPath, cause) : Map(spec, specDllPath, cause);
    }

    // The message carries the missing name for both failures: a missing member quotes its signature, whose return
    // or parameter type is where a moved type appears, and a missing type quotes the type. TypeName is read too,
    // because it is the field the runtime is sure to fill for a type that failed to load.
    private static bool NamesMovedSelectionType(Exception cause)
    {
        string typeName = cause is TypeLoadException typeLoad ? typeLoad.TypeName : "";

        return MovedSelectionTypes.Any(moved => cause.Message.Contains(moved, StringComparison.Ordinal)
                                                || string.Equals(typeName, moved, StringComparison.Ordinal));
    }

    /// <summary>
    ///     True when the member a missing-member <paramref name="message" /> names is declared by a type this
    ///     host's copy of the contract defines, and false for any other member or a message it cannot read.
    /// </summary>
    /// <remarks>
    ///     The runtime fills neither <see cref="MissingMemberException" /> name property, so the declaring type is
    ///     read out of the quoted signature: <c>'Void SlideRule.Arch.FutureVerb(System.String)'</c> for a
    ///     method, <c>'SlideRule.Arch.FutureField'</c> for a field. Keying on the quotes rather than the
    ///     English around them keeps the parse independent of that wording. The parameter list goes first and the
    ///     return type next, because either may name a contract type around a member that is not the contract's.
    ///     A constructor reads <c>Arch..ctor</c>, hence the trailing dot trimmed; a generic method carries no
    ///     suffix. A nested or generic type prints in a form the lookup cannot resolve (<c>Inner.Verb</c>,
    ///     <c>Box`1.Verb</c>), which costs nothing while the contract declares no public type of either kind, and
    ///     the lookup is what keeps a rule pack under the contract's root namespace out. It must not throw (see
    ///     <see cref="SkewCause" />), so a name the type parser treats specially never reaches it.
    /// </remarks>
    private static bool IsContractMember(string message)
    {
        int open = message.IndexOf('\'');
        int close = message.LastIndexOf('\'');
        if (open < 0 || close <= open) return false;

        string signature = message[(open + 1)..close];
        int parameters = signature.IndexOf('(');
        if (parameters >= 0) signature = signature[..parameters];

        string qualifiedMember = signature[(signature.LastIndexOf(' ') + 1)..];
        int memberDot = qualifiedMember.LastIndexOf('.');
        if (memberDot < 0) return false;

        string declaringType = qualifiedMember[..memberDot]
            .TrimEnd('.');
        return IsUnderContractRoot(declaringType)
               && declaringType.IndexOfAny(TypeNameSyntax) < 0
               && ContractDefines(declaringType);
    }

    private static bool IsUnderContractRoot(string typeName)
    {
        return typeName.StartsWith(typeof(IArchitectureSpec).Namespace + ".", StringComparison.Ordinal);
    }

    private static bool ContractDefines(string typeName)
    {
        return typeof(IArchitectureSpec).Assembly.GetType(typeName, false) is not null;
    }

    /// <summary>
    ///     The first of <paramref name="loaderExceptions" /> that names a type this host's contract lacks, or
    ///     <see langword="null" /> when none does.
    /// </summary>
    private static TypeLoadException? MissingContractType(IEnumerable<Exception?> loaderExceptions)
    {
        return loaderExceptions
            .OfType<TypeLoadException>()
            .FirstOrDefault(exception => IsMissingContractType(exception.TypeName));
    }

    /// <summary>
    ///     The parenthetical naming both contract versions, or empty when they are identical — which is
    ///     the normal case, since the shipped contract identity is pinned rather than tracking the package
    ///     version. A delta only shows for a spec built against a pre-pin package or a source-built
    ///     contract, and there it is the whole explanation, so it leads.
    /// </summary>
    private static string DescribeDelta(Assembly spec)
    {
        Version? host = typeof(IArchitectureSpec).Assembly.GetName().Version;
        Version? referenced = spec.GetReferencedAssemblies()
            .FirstOrDefault(a => a.Name == SpecLoadContext.ContractAssemblyName)?.Version;

        if (host is null || referenced is null || referenced.Equals(host)) return "";

        return $" (the spec was built against contract {referenced}, this tool carries {host})";
    }
}
