using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using SlideRule.Cli.SpecLoading;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     A compile-time stand-in for the <c>SlideRule</c> contract at a chosen
///     <see cref="Version" /> — the one thing the suite cannot get from the real core, whose
///     <c>AssemblyVersion</c> is pinned to <c>1.0.0.0</c> on purpose. A spec compiled against it carries an
///     assembly ref to that identity, which is what makes the version-agnostic bind in
///     <see cref="SpecLoadContext.Load" /> (and the <c>SpecContractMismatch</c> failure it defers to)
///     testable end to end, and what lets the adapter skew probe load the xUnit adapter beside a contract
///     of another version.
/// </summary>
/// <remarks>
///     <para>
///         Nothing on disk ever carries a second copy of these type names: the image stays in memory, as a
///         <see cref="MetadataReference" /> or as bytes. The one load is the adapter skew probe's, into a
///         collectible load context of its own that the probe unloads, so the host's own contract is never
///         displaced. The source is a string constant, so <c>MSBuildWorkspace</c> sees a literal and the
///         dogfood universe (self-spec subjects, <c>arch_graph</c> inventories, the <c>AGENTS.md</c> render)
///         is untouched.
///     </para>
///     <para>
///         <b>Every member here must match the real declaration exactly.</b> The memberrefs a spec emits
///         against this stub are resolved at run time against the host's real contract, so a drifted
///         signature does not go unnoticed — it reds the <em>green</em> arms of
///         <c>SpecContractSkewE2ETests</c> with the runtime naming the exact member that failed to resolve.
///         That is the guard doing its job, not a gap in it. The caller-info optionals on
///         <see cref="Arch.Rule" /> are part of that: without them the call site binds a one-argument
///         <c>Rule(string)</c> the host does not declare.
///     </para>
///     <para>
///         Three declarations here are ones the host deliberately does not have, and between them they cover
///         every way a spec can reach past its contract. <c>FutureVerb</c> is a member on a <em>present</em>
///         type, so the runtime reports a <see cref="MissingMethodException" /> out of <c>Define()</c>.
///         <c>FutureConstraints</c> is a whole type, which fails earlier, at typeref resolution, as a
///         <see cref="TypeLoadException" />, whether the call sits in <c>Define()</c> or in the constructor of a
///         spec class. <c>IFutureShape</c> is there for one of the spec's own types to implement, so that type fails to
///         load while the spec's types are discovered, before <c>Define()</c> runs. All three must reach
///         <c>SpecContractMismatch</c>.
///     </para>
/// </remarks>
internal static class SkewedContract
{
    // Only the surface the skew specs touch. Bodies never run: the CLI only references this assembly, and the
    // adapter skew probe loads it but calls nothing on it.
    private const string Source = """
                                  #nullable enable
                                  using System;
                                  using System.Reflection;
                                  using System.Runtime.CompilerServices;
                                  using SlideRule.Fluent;

                                  [assembly: AssemblyVersion("{VERSION}")]
                                  [assembly: AssemblyInformationalVersion("{VERSION}")]

                                  namespace SlideRule.Fluent
                                  {
                                      public interface IEnforceRule
                                      {
                                          IEnforceRule Because(string because);
                                      }
                                  }

                                  namespace SlideRule
                                  {
                                      public abstract class Selection
                                      {
                                      }

                                      public abstract class Constraint
                                      {
                                      }

                                      public interface IArchitectureSpec
                                      {
                                          void Define(Arch arch);
                                      }

                                      public interface IRuleBuilder
                                      {
                                          IEnforceRule Enforce(Constraint constraint);
                                      }

                                      public sealed class Arch
                                      {
                                          public Selection Types => throw new NotSupportedException();

                                          public IRuleBuilder Rule(string id,
                                              [CallerFilePath] string? filePath = null, [CallerLineNumber] int lineNumber = 0)
                                          {
                                              throw new NotSupportedException();
                                          }

                                          public void FutureVerb(string note)
                                          {
                                              throw new NotSupportedException();
                                          }
                                      }

                                      public static class SelectionConstraints
                                      {
                                          public static Constraint MustHaveSuffix(this Selection subject, string suffix)
                                          {
                                              throw new NotSupportedException();
                                          }
                                      }

                                      public static class FutureConstraints
                                      {
                                          public static Constraint MustBeFuture(this Selection subject)
                                          {
                                              throw new NotSupportedException();
                                          }
                                      }

                                      public interface IFutureShape
                                      {
                                      }
                                  }
                                  """;

    // The emitted image is a pure function of the version and a MetadataReference is immutable, so one stored
    // image per distinct version serves the whole run rather than one per request, and serves both forms. Two
    // racing first requests may each compile it, but both get the one GetOrAdd keeps.
    private static readonly ConcurrentDictionary<Version, Emitted> ByVersion = new();

    /// <summary>
    ///     The stand-in contract at <paramref name="version" />, as a reference to compile a spec against.
    /// </summary>
    internal static MetadataReference Reference(Version version)
    {
        return ByVersion.GetOrAdd(version, Emit)
            .MetadataReference;
    }

    /// <summary>
    ///     The stand-in contract at <paramref name="version" />, as the emitted image, for a probe that loads it
    ///     into a load context of its own. The array is shared; never write to it.
    /// </summary>
    internal static byte[] Image(Version version)
    {
        return ByVersion.GetOrAdd(version, Emit)
            .Bytes;
    }

    private static Emitted Emit(Version version)
    {
        byte[] image = SpecAssemblyCompiler.EmitImage(
            Source.Replace("{VERSION}", version.ToString(), StringComparison.Ordinal),
            SpecLoadContext.ContractAssemblyName,
            SpecAssemblyCompiler.PlatformReferences);
        MetadataReference reference = MetadataReference.CreateFromImage(image);
        return new Emitted(image, reference);
    }

    private sealed record Emitted(byte[] Bytes, MetadataReference MetadataReference);
}
