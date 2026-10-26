using SlideRule.Codebase;

namespace SlideRule.Rendering;

/// <summary>
///     How a member is spelled in a rendered line: its declaring type, a dot, its name, and <c>()</c> iff it
///     is a method.
/// </summary>
/// <remarks>
///     The spelling is one primitive rather than a per-surface choice, because it is not only rendered:
///     <c>baseline add</c> resolves names against it and lists candidates in it, so a form that drifted from
///     what <c>check</c> prints would ask an operator to retype a name the product never showed them.
/// </remarks>
internal static class MemberDisplay
{
    internal static string Of(MemberReference member)
    {
        return $"{member.ContainingType.FullName}.{member.Name}{Suffix(member.Kind)}";
    }

    internal static string Of(MemberNode member)
    {
        return $"{member.DeclaringType.FullName}.{member.Name}{Suffix(member.Kind)}";
    }

    private static string Suffix(MemberKind kind)
    {
        return kind == MemberKind.Method ? "()" : string.Empty;
    }
}
