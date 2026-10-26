using System.Security.Cryptography;
using System.Text;

namespace SlideRule.Baselines;

/// <summary>
///     Seals baseline entries, holding one hash instance across all of them.
/// </summary>
/// <remarks>
///     A seal is per entry rather than per file, so composing or verifying a file of a thousand entries
///     asks for a thousand hashes. Creating a <see cref="SHA256" /> for each one costs more than taking
///     any of them; one instance, reused, keeps the per-entry cost to the hash itself. Not thread-safe,
///     which is why it is passed down one composition or one read rather than held anywhere: a hash
///     instance carries state between <c>ComputeHash</c> calls.
/// </remarks>
internal sealed class BaselineSealer : IDisposable
{
    private readonly SHA256 _sha = SHA256.Create();

    public void Dispose()
    {
        _sha.Dispose();
    }

    /// <summary>
    ///     The <c>seal</c> <paramref name="entry" /> carries in the section for <paramref name="ruleId" />: the
    ///     first 16 lowercase hex characters of the SHA-256 of the UTF-8 bytes of
    ///     <see cref="BaselineFormat.SealInput" />. A stored entry whose own <c>seal</c> differs from this was
    ///     edited by hand rather than written by <see cref="BaselineFormat.ComposeFile" />.
    /// </summary>
    internal string Seal(string ruleId, BaselineEntry entry)
    {
        string input = BaselineFormat.SealInput(ruleId, entry);
        byte[] hash = _sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        return BaselineFormat.ToLowerHex(hash, BaselineFormat.SealBytes);
    }
}
