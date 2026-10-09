namespace SlideRule.Extraction.Fragments;

/// <summary>
///     One package a project declares, as pure data: the package identifier, the <c>file:line</c> of the
///     declaration, and whether the reference is private — its evaluated <c>PrivateAssets</c> covering every
///     asset, so the package reaches neither the project's own package nor a project referencing it. The
///     serializable counterpart of <see cref="SlideRule.Codebase.PackageReference" />.
/// </summary>
/// <remarks>
///     The declaring file is regularly not the project file — a <c>Directory.Build.props</c> above it can
///     add a package to every project beneath — which is why the site travels with the name rather than
///     being reconstructed from the project at merge. Privacy travels with them for the same reason: an item
///     definition or an <c>Update</c> in another file can decide it, and only the evaluation that read the
///     declaration saw which.
/// </remarks>
internal readonly record struct FragmentPackageReference(string Name, FragmentSite Site, bool IsPrivate);
