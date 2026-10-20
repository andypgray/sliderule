namespace SlideRule.Rendering;

/// <summary>
///     One agent-context file a render produces. The body is already merged, so a directory hosting both a
///     layer card and a scope card yields one file carrying both and there is nothing left to combine.
/// </summary>
internal sealed class ContextFile
{
    internal ContextFile(string path, string body)
    {
        Path = path;
        Body = body;
    }

    /// <summary>Gets the absolute path of the <c>AGENTS.md</c> this body belongs in.</summary>
    public string Path { get; }

    /// <summary>
    ///     Gets the composed managed-block body, LF throughout: the provenance line first, then the cards.
    /// </summary>
    public string Body { get; }
}
