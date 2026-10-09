namespace MultiTfm.Core
{
    // Compiled under BOTH frameworks, so the two compilations declare one FQN: the collapse the
    // multi-framework merge note is about. Block-scoped namespace because netstandard2.0 defaults to
    // C# 7.3, which has no file-scoped namespaces. The framework-conditional half of the class lives in
    // Widget.Legacy.cs, so this file's token shape agrees across frameworks and a trivia-only edit to it
    // remaps.
    public partial class Widget
    {
    }
}
