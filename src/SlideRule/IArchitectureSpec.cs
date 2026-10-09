namespace SlideRule;

/// <summary>
///     A unit of architecture specification. Implement it as a public, non-abstract class with a public
///     parameterless constructor in the spec project. Every such class in the assembly is discovered by
///     reflection and created through that constructor. Its <see cref="Define" /> runs once per model
///     build, in ordinal name order, on the single <see cref="Arch" /> that every spec class of that
///     build shares.
/// </summary>
public interface IArchitectureSpec
{
    /// <summary>
    ///     Declares the spec's layers, rules and scopes on <paramref name="arch" />. Nothing is evaluated
    ///     here: a rule is data, checked later by the CLI or the test adapter.
    /// </summary>
    void Define(Arch arch);
}
