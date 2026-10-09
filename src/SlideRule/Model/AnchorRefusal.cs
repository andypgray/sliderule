namespace SlideRule.Model;

/// <summary>Why a <see cref="TypeAnchor" /> cannot supply the key a position asks it for.</summary>
internal enum AnchorRefusal
{
    None,
    ClosedGeneric,
    Unrepresentable
}
