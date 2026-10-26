using System.Text;
using SlideRule.Internal;
using SlideRule.Roslyn.Caching;

namespace SlideRule.Cli.Hooks;

/// <summary>What <c>sliderule hook</c> keeps between the firings of one session, in a directory of its own.</summary>
/// <remarks>
///     No verdict depends on it: delete it and the next stop checks. A read that fails comes back empty and a
///     write that fails is dropped, except the round count, which the caller is told about because the round cap
///     rests on it. A session whose directory cannot be made is stateless, where <see cref="Stateful" /> is
///     false, every read is empty and every write does nothing, so the exit mapping reads the same whether or not
///     there is anywhere to keep state. The files are plain text, one fact per line, with a tree kept as its HEAD
///     and then one <c>&lt;digest&gt; &lt;path&gt;</c> line per changed code file. A record that does not read
///     that way, such as one an older build wrote, reads as no record.
/// </remarks>
internal sealed class HookState
{
    private const string VerdictFile = "verdict";
    private const string RoundsFile = "rounds";
    private const string BaselineFile = "baseline";
    private const string SaidFile = "said";
    private const string TreeFile = "tree";
    private const string TreesDirectory = "trees";
    private const string GreenWord = "green";
    private const string RedWord = "red";
    private const string PromptWord = "prompt";
    private const string StopWord = "stop";

    private readonly string? _directory;
    private readonly string _said;

    private HookState(string? directory, string said)
    {
        _directory = directory;
        _said = said;
    }

    /// <summary>No state at all.</summary>
    public static HookState None { get; } = new(null, SaidFile);

    /// <summary>Whether there is a directory to keep state in.</summary>
    public bool Stateful => _directory is not null;

    /// <summary>
    ///     The state of session <paramref name="sessionId" /> under <paramref name="root" />, in
    ///     <c>sliderule-hook/&lt;session&gt;</c>, with what was said kept apart for subagent
    ///     <paramref name="agentId" />: a worker's warnings are in the worker's context, not the main agent's
    ///     (verified 2026-10-20 on Claude Code 2.1.300).
    /// </summary>
    /// <returns>The session's state, or <see cref="None" /> when its directory cannot be made.</returns>
    public static HookState Open(string root, string? sessionId, string? agentId)
    {
        string directory = Path.Combine(root, "sliderule-hook", sessionId ?? "nosession");
        if (!TryCreate(directory)) return None;

        return new HookState(directory, agentId is null ? SaidFile : $"{SaidFile}-{agentId}");
    }

    /// <summary>
    ///     The state this session keeps for <paramref name="topLevel" />, another tree of the project that a turn
    ///     wrote into: a directory of its own under the session's, keyed by a digest of the top level.
    /// </summary>
    /// <remarks>
    ///     The verdict, the baseline and a <c>tree</c> file holding <paramref name="topLevel" /> are kept there. The
    ///     digest cannot be turned back into a path, so that file is what <see cref="RecordedTrees" /> reads. The
    ///     round count and what an agent was told belong to the stop, however many trees it checks, so they stay on
    ///     the session's own state.
    /// </remarks>
    /// <returns>
    ///     The tree's state, or <see cref="None" /> when the session has none or the directory cannot be made.
    /// </returns>
    public HookState ForTree(string topLevel)
    {
        if (_directory is null) return None;

        string identity = CacheLocations.PathIdentity(PathComparison.Fold(topLevel));
        string directory = Path.Combine(_directory, TreesDirectory, identity);
        if (!TryCreate(directory)) return None;

        var tree = new HookState(directory, SaidFile);
        if (tree.ReadTreeName() != topLevel) tree.Write(TreeFile, topLevel + "\n");

        return tree;
    }

    /// <summary>
    ///     The other trees this session keeps records for: each top level as <see cref="ForTree" /> was given it,
    ///     with the state kept for it. A record that names no tree, such as one an older build wrote, is not among
    ///     them.
    /// </summary>
    /// <returns>The recorded trees, or none when the session has no state or its records cannot be listed.</returns>
    public IReadOnlyList<(string TopLevel, HookState State)> RecordedTrees()
    {
        if (_directory is null) return [];

        string trees = Path.Combine(_directory, TreesDirectory);
        try
        {
            if (!Directory.Exists(trees)) return [];

            var recorded = new List<(string TopLevel, HookState State)>();
            foreach (string directory in Directory.EnumerateDirectories(trees))
            {
                var tree = new HookState(directory, SaidFile);
                if (tree.ReadTreeName() is { } topLevel) recorded.Add((topLevel, tree));
            }

            return recorded;
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            return [];
        }
    }

    /// <summary>
    ///     The last verdict and the tree it covered, or null when none was recorded or it cannot be read.
    /// </summary>
    public HookVerdict? ReadVerdict()
    {
        return ReadRecord(VerdictFile, GreenWord, RedWord) is { } record
            ? new HookVerdict(record.Flag, record.Tree)
            : null;
    }

    /// <summary>
    ///     The baseline standing, or null when none stands or it cannot be read: the tree the prompt in flight found,
    ///     or the tree the last turn's stop left until the next prompt replaces it.
    /// </summary>
    public HookBaseline? ReadBaseline()
    {
        return ReadRecord(BaselineFile, PromptWord, StopWord) is { } record
            ? new HookBaseline(record.Tree, record.Flag)
            : null;
    }

    public void RecordVerdict(bool green, TreeSnapshot tree)
    {
        string word = green ? GreenWord : RedWord;
        Write(VerdictFile, $"{word}\n{TreeText(tree)}");
    }

    /// <summary>Records <paramref name="tree" /> as the tree the prompt in flight found.</summary>
    public void RecordBaseline(TreeSnapshot tree)
    {
        Write(BaselineFile, $"{PromptWord}\n{TreeText(tree)}");
    }

    /// <summary>
    ///     Ends the turn's baseline by putting <paramref name="tree" />, the tree the turn's stop left, in its place:
    ///     a subagent that stops after the turn has ended is compared with it, and the next prompt replaces it. An
    ///     unknown tree leaves no baseline at all.
    /// </summary>
    public void RollBaseline(TreeSnapshot tree)
    {
        if (tree.Known)
            Write(BaselineFile, $"{StopWord}\n{TreeText(tree)}");
        else
            Delete(BaselineFile);
    }

    /// <summary>
    ///     How many consecutive continuations prompt <paramref name="promptId" /> has had: zero for a count kept
    ///     for another prompt, for no count at all, and for one that will not parse.
    /// </summary>
    public int RoundsFor(string promptId)
    {
        string? counted = ReadText(RoundsFile)
            ?.TrimEnd('\r', '\n');
        if (counted is null || !counted.StartsWith(promptId + " ", StringComparison.Ordinal)) return 0;

        string number = counted[(counted.LastIndexOf(' ') + 1)..];
        bool digits = number.Length > 0 && number.All(char.IsAsciiDigit);
        return digits && int.TryParse(number, out int rounds) ? rounds : 0;
    }

    /// <summary>
    ///     Counts one more continuation for prompt <paramref name="promptId" />, after <paramref name="rounds" /> of
    ///     them.
    /// </summary>
    /// <returns>
    ///     True when the count reads back, false where it does not: no directory, a write that was dropped, or a
    ///     read that fails. A directory that exists but keeps nothing answers false here while still reading as
    ///     <see cref="Stateful" />.
    /// </returns>
    public bool CountRound(string promptId, int rounds)
    {
        Write(RoundsFile, $"{promptId} {rounds + 1}\n");
        return RoundsFor(promptId) == rounds + 1;
    }

    /// <summary>Resets the count: the continuations are over.</summary>
    public void ForgetRounds()
    {
        Delete(RoundsFile);
    }

    /// <summary>Whether this agent was last told exactly <paramref name="text" />.</summary>
    public bool AlreadySaid(string text)
    {
        string? said = ReadText(_said);
        return said is not null && string.Equals(said.TrimEnd('\r', '\n'), text, StringComparison.Ordinal);
    }

    /// <summary>Records <paramref name="text" /> as what this agent was last told.</summary>
    public void RememberSaid(string text)
    {
        Write(_said, text + "\n");
    }

    /// <summary>Forgets what this agent was told, so warnings that lapse and come back are said again.</summary>
    public void ForgetSaid()
    {
        Delete(_said);
    }

    // The top level a tree's records name: their tree file's one line, or null for a file that is missing, blank or
    // holds more than one line.
    private string? ReadTreeName()
    {
        string? name = ReadText(TreeFile)
            ?.TrimEnd('\r', '\n');
        return string.IsNullOrWhiteSpace(name) || name.Contains('\n') ? null : name;
    }

    // A verdict or baseline record: a leading word, true for `trueWord` and false for `falseWord`, then the tree.
    // Null when the file is missing or does not read that way.
    private (bool Flag, TreeSnapshot Tree)? ReadRecord(string name, string trueWord, string falseWord)
    {
        if (ReadLines(name) is not { } lines) return null;

        string word = lines[0];
        bool? flag = word == trueWord ? true : word == falseWord ? false : null;
        if (flag is null || TreeFrom(lines) is not { } tree) return null;

        return (flag.Value, tree);
    }

    // The digest goes first because a path may hold a space and a digest never does.
    private static string TreeText(TreeSnapshot tree)
    {
        var text = new StringBuilder();
        text.Append(tree.Head)
            .Append('\n');
        foreach (string path in tree.Paths)
            text.Append(tree.DigestOf(path))
                .Append(' ')
                .Append(path)
                .Append('\n');

        return text.ToString();
    }

    // The tree after a record's leading word, or null when a line does not read as TreeText writes one.
    private static TreeSnapshot? TreeFrom(string[] lines)
    {
        if (lines.Length < 2 || lines[1].Length == 0) return null;

        var files = new List<(string Path, string Digest)>();
        foreach (string line in lines.Skip(2)
                     .Where(static line => line.Length > 0))
        {
            int space = line.IndexOf(' ');
            if (space < 0 || space == line.Length - 1 || !IsDigest(line[..space])) return null;

            files.Add((line[(space + 1)..], line[..space]));
        }

        return TreeSnapshot.Recorded(lines[1], files);
    }

    // What TreeSnapshot.Digest writes: one of its two markers, or SHA-256 as 64 lowercase hex digits.
    private static bool IsDigest(string token)
    {
        return token is TreeSnapshot.Absent or TreeSnapshot.Unreadable
               || (token.Length == 64 && token.All(static c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f'));
    }

    private string[]? ReadLines(string name)
    {
        return ReadText(name)
            ?.Split('\n');
    }

    private string? ReadText(string name)
    {
        if (_directory is null) return null;

        try
        {
            string path = PathOf(name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            return null;
        }
    }

    private void Write(string name, string text)
    {
        if (_directory is null) return;

        try
        {
            File.WriteAllText(PathOf(name), text);
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            // Dropped: the file keeps its old record or none, which costs at most a check or a repeated warning. A
            // round count dropped here reads back short, and CountRound reports that.
        }
    }

    private void Delete(string name)
    {
        if (_directory is null) return;

        try
        {
            File.Delete(PathOf(name));
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            // Left in place: a stale count or baseline costs a stand-down or a check, and a stale record of what
            // was said keeps a warning that lapsed and came back unchanged from being said again. None of them
            // blocks wrongly.
        }
    }

    private string PathOf(string name)
    {
        return Path.Combine(_directory!, name);
    }

    // Whether a directory could be made, or already stood.
    private static bool TryCreate(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            return true;
        }
        catch (Exception ex) when (IsFileSystemFailure(ex))
        {
            return false;
        }
    }

    /// <summary>
    ///     Whether <paramref name="ex" /> is a file read, write or path failing, which the hook reads as doubt
    ///     rather than as its own failure.
    /// </summary>
    internal static bool IsFileSystemFailure(Exception ex)
    {
        return ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
    }
}

/// <summary>A verdict read back from the hook's state: whether the check was green, and over which tree.</summary>
internal sealed record HookVerdict(bool Green, TreeSnapshot Tree);

/// <summary>A baseline read back from the hook's state: a tree, and whether a prompt or a stop recorded it.</summary>
internal sealed record HookBaseline(TreeSnapshot Tree, bool FromPrompt);
