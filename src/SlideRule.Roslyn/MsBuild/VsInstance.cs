namespace SlideRule.Roslyn.MsBuild;

/// <summary>The fields of one <c>vswhere</c> entry that MSBuild selection needs.</summary>
internal sealed record VsInstance(string InstallationPath, Version Version, string Name);
