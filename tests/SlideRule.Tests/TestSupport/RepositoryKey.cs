using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Compilation options for an in-memory compilation whose identity must carry this repository's strong-name
///     key: one that takes the name of a friend assembly to reach the contract's internals, or one that stands in
///     for the contract itself.
/// </summary>
/// <remarks>
///     The contract's InternalsVisibleTo grants name each friend together with the repository's public key, so a
///     compilation that borrows a friend's name is refused (CS0281) unless its identity carries the same key. A
///     stand-in contract needs it for the other direction: every signed assembly here references the contract by
///     its public key token, and a reference to an unsigned stand-in does not unify with it (CS0012). Public
///     signing gives either identity from the public half alone. The key is read from the loaded contract, so it
///     cannot disagree with the grants or the references.
/// </remarks>
internal static class RepositoryKey
{
    private static readonly ImmutableArray<byte> PublicKey =
        [.. typeof(Arch).Assembly.GetName().GetPublicKey() ?? []];

    /// <summary>The options, public-signed with the key every assembly in this repository is signed with.</summary>
    /// <param name="options">The options to sign.</param>
    internal static CSharpCompilationOptions WithRepositoryKey(this CSharpCompilationOptions options)
    {
        return options
            .WithCryptoPublicKey(PublicKey)
            .WithPublicSign(true);
    }
}
