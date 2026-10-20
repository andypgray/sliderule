namespace SlideRule.Extraction.Fragments;

/// <summary>
///     One type a single compilation declares, its hierarchy by FQN. Its project is the declaring fragment's
///     <see cref="CodebaseFragment.ProjectName" />, so it is not repeated here.
/// </summary>
/// <remarks>
///     Each list's order is part of the serialized form and must survive a round trip:
///     <see cref="Interfaces" /> and <see cref="Attributes" /> are in symbol order, unsorted;
///     <see cref="AllInterfaces" /> and <see cref="AttributeConstructions" /> are ordinal by constructed name;
///     <see cref="BaseTypeChain" /> is nearest-first and never sorted; <see cref="DeclaredMembers" /> is
///     ordinal by <see cref="MemberFacts.SymbolId" />. <see cref="BaseTypeFullName" /> is null for interfaces
///     and <see cref="object" />.
/// </remarks>
internal sealed record FragmentType(
    TypeFacts Facts,
    IReadOnlyList<FragmentSite> DeclarationSites,
    string? BaseTypeFullName,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<string> Attributes,
    IReadOnlyList<FragmentConstruction> AllInterfaces,
    IReadOnlyList<FragmentConstruction> BaseTypeChain,
    IReadOnlyList<FragmentConstruction> AttributeConstructions,
    IReadOnlyList<FragmentMember> DeclaredMembers);
