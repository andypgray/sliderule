using System.Reflection;
using System.Runtime.Loader;

namespace SlideRule.Cli.SpecLoading;

/// <summary>
///     A collectible <see cref="AssemblyLoadContext" /> that isolates a prebuilt spec DLL — the
///     host-side half of loading a spec in isolation. The type-identity fix is the shared-contract
///     short-circuit: <c>SlideRule</c> resolves to the copy this host already carries so
///     <c>spec is IArchitectureSpec</c> holds across the boundary; everything else resolves through
///     the dependency resolver.
/// </summary>
/// <remarks>
///     Every assembly this context loads is loaded from its <em>bytes</em>, never its path — see
///     <see cref="LoadWithoutLocking" />. The context is collectible, but the model it produces roots
///     the spec's <c>Type</c> references, so it is never actually collected while that model lives;
///     a path load would therefore hold an OS file lock for the whole host lifetime.
/// </remarks>
internal sealed class SpecLoadContext : AssemblyLoadContext, IDisposable
{
    /// <summary>The spec contract's simple name — the one assembly identity shared across the boundary.</summary>
    internal const string ContractAssemblyName = "SlideRule";

    /// <summary>The contract this host carries, whatever version a spec was compiled against.</summary>
    private static readonly Assembly Contract = typeof(IArchitectureSpec).Assembly;

    private readonly AssemblyDependencyResolver _resolver;

    internal SpecLoadContext(string mainAssemblyPath)
        : base(true)
    {
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    /// <summary>Starts unloading the context.</summary>
    public void Dispose()
    {
        Unload();
    }

    /// <summary>
    ///     Loads an assembly from its bytes, so the file is not locked for the rest of this process's
    ///     lifetime.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A path that is not on disk is handed to the loader rather than opened as a stream, so the
    ///         failure is the loader's own <see cref="FileNotFoundException" /> naming that path. That is the
    ///         spec DLL itself going missing between resolution and load — a rebuild landing underneath a
    ///         warm host — and not the missing-dependency case: <see cref="AssemblyDependencyResolver" />
    ///         re-checks the disk, so a dependency that has gone resolves to null and the Default context's
    ///         own failure is what surfaces, carrying the assembly display identity
    ///         <c>SpecDependencyLoadFailure.IsAssemblyLoadFailure</c> keys on. Both shapes are pinned by
    ///         <c>SpecLoadContextFallbackTests</c>.
    ///     </para>
    ///     <para>
    ///         <see cref="FileShare" /> admits writers and deleters: a concurrent build may replace the file
    ///         while these bytes are in flight, and the copy already read stays valid.
    ///     </para>
    /// </remarks>
    internal Assembly LoadWithoutLocking(string path)
    {
        if (!File.Exists(path)) return LoadFromAssemblyPath(path);

        using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return LoadFromStream(stream);
    }

    /// <summary>
    ///     Resolves the contract to this host's own copy by simple name, and everything else through the
    ///     spec's dependency manifest.
    /// </summary>
    /// <remarks>
    ///     Returning the contract assembly here rather than null is what makes the bind version-agnostic: the
    ///     Default context would hand back the same assembly but also enforce requested-version ≤
    ///     available-version, so a spec compiled against a newer contract would fail to bind at all. A spec
    ///     that turns out to need contract API this copy does not have fails later, in <c>Define()</c>, while
    ///     its own types load or while a spec class is created, where <see cref="SpecContractMismatch" /> can
    ///     name both versions and the remedy.
    /// </remarks>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name == ContractAssemblyName) return Contract;

        string? path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is not null ? LoadWithoutLocking(path) : null;
    }
}
