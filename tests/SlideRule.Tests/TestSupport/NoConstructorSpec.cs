namespace SlideRule.Tests.TestSupport;

/// <summary>
///     A spec DLL whose one spec class has no public parameterless constructor, and the refusal every
///     surface must print for it. The class is compiled on demand rather than declared in this assembly,
///     because the discovery tests scan this assembly for specs and would meet it too.
/// </summary>
internal static class NoConstructorSpec
{
    /// <summary>The refusal the CLI and the MCP server print for the emitted spec, verbatim.</summary>
    internal const string Refusal =
        "Spec class 'NoCtorProbe.NoCtorSpec' has no public parameterless constructor. SlideRule creates one "
        + "instance of each public IArchitectureSpec class it finds, so declare a public constructor that takes "
        + "no arguments, or make the class abstract if it is a base for other specs.";

    private const string Source =
        """
        using SlideRule;

        namespace NoCtorProbe
        {
            public sealed class NoCtorSpec : IArchitectureSpec
            {
                public NoCtorSpec(string name)
                {
                }

                public void Define(Arch arch)
                {
                }
            }
        }
        """;

    /// <summary>Compiles the spec into a fresh temp directory named for <paramref name="category" />.</summary>
    internal static string EmitDll(string category)
    {
        return SpecAssemblyCompiler.EmitSpecDllToFreshTemp(category, "NoCtor", Source);
    }
}
