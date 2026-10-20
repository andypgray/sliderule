namespace SlideRule.Extraction.Fragments;

/// <summary>
///     One declared method parameter (GRAMMAR §4.6, §5.6), its facts meaning what
///     <see cref="SlideRule.IParameterInfo" /> documents: <see cref="TypeFullName" /> is definition-level, as
///     <see cref="MemberFacts.ReturnTypeFullName" /> is, so a <c>MustAcceptParameter</c> anchor matches at the
///     definition level.
/// </summary>
internal sealed record ParameterFacts(
    string Name,
    string TypeFullName);
