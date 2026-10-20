namespace SlideRule.Extraction.Fragments;

/// <summary>
///     One package a project declares, with the <c>file:line</c> of its declaration and whether it is private:
///     its evaluated <c>PrivateAssets</c> covers every asset, so the package reaches neither the project's own
///     package nor a project referencing it. The serializable counterpart of
///     <see cref="SlideRule.Codebase.PackageReference" />.
/// </summary>
/// <remarks>
///     Both facts are the evaluation's, because another file can decide either: a <c>Directory.Build.props</c>
///     above the project can declare the package, and an item definition or an <c>Update</c> can make it private.
/// </remarks>
internal readonly record struct FragmentPackageReference(string Name, FragmentSite Site, bool IsPrivate);
