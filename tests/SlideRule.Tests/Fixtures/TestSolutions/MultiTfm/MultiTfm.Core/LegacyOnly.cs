#if !NET
namespace MultiTfm.Core.Legacy
{
    // Compiled under netstandard2.0 only (NET is defined on net10.0 and absent there): the mirror of
    // ModernOnly. It exists so a type both frameworks share can make a reference that only the losing
    // framework's compilation makes. Block-scoped namespace because netstandard2.0 defaults to C# 7.3.
    public class LegacyOnly
    {
    }
}
#endif
