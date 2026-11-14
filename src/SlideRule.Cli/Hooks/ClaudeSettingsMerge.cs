using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using System.Text.Json.Nodes;
using SlideRule.Cli.Rendering;
using SlideRule.Cli.Verbs;
using SlideRule.Roslyn;

namespace SlideRule.Cli.Hooks;

/// <summary>
///     Merges a settings template's hook entries and deny rules into a Claude Code <c>settings.json</c>, keeping
///     every other setting and the order it stands in.
/// </summary>
/// <remarks>
///     A template matcher group is present when the target's event holds a group with the same matcher, an absent
///     one counting as its own value, and one of that group's commands runs the <c>hook</c> verb, in exec form
///     (<c>command</c> plus <c>args</c>) or shell form (one <c>command</c> string). A present group is kept as the
///     user has it. A deny rule is present when an ordinal-equal string stands in the target's list. Nothing is
///     reordered or removed, and a container is created only where it is absent.
/// </remarks>
internal static class ClaudeSettingsMerge
{
    private const string HooksKey = "hooks";
    private const string PermissionsKey = "permissions";
    private const string DenyKey = "deny";
    private const string MatcherKey = "matcher";
    private const string CommandKey = "command";
    private const string ArgsKey = "args";

    /// <summary>
    ///     <paramref name="existing" /> (null when there is no file) with whatever of <paramref name="template" /> it
    ///     lacks; every hook entry of the template is in exec form. <paramref name="solution" /> and
    ///     <paramref name="spec" />, relative to the root the hook runs from and each null to leave it out, are appended
    ///     to the <c>args</c> of every entry an added group carries.
    ///     <paramref name="commandTree" /> is the CLI's command tree, which an existing command line is parsed against.
    ///     <paramref name="filePath" /> names the file in a refusal.
    /// </summary>
    /// <exception cref="UserErrorException">
    ///     The file is not JSON, holds something other than an object where the merge must write, or a path cannot be
    ///     written into a hook entry.
    /// </exception>
    /// <exception cref="InvalidOperationException">A hook entry of the template has no <c>args</c> array.</exception>
    public static Result Merge(
        string? existing, string template, string? solution, string? spec, Command commandTree, string filePath)
    {
        string[] suffix = CommandArguments(solution, spec)
            .ToArray();
        JsonObject target = Parse(existing, filePath);
        var snippet = (JsonObject)JsonNode.Parse(template)!;

        var denied = new List<string>();
        var hooked = new List<string>();
        var added = 0;
        if (snippet[PermissionsKey]?[DenyKey] is JsonArray templateDeny)
            added += MergeDeny(target, templateDeny, denied, filePath);
        if (snippet[HooksKey] is JsonObject templateHooks)
            added += MergeHooks(target, templateHooks, suffix, commandTree, hooked, filePath);

        string text = SlideRuleJson.Terminated(target.ToJsonString(SlideRuleJson.Options));
        return new Result(text, [.. hooked, .. denied], added > 0);
    }

    // Whether an entry's argument vector runs the hook verb: the tool's name, then arguments that parse to that verb
    // and invoke its own action rather than an option's, such as help.
    private static bool RunsHookRunner(Command commandTree, string[] tokens)
    {
        if (tokens is not [CommandFactory.ToolCommandName, .. var arguments]) return false;

        ParseResult parsed = commandTree.Parse(arguments);
        return parsed.Errors.Count == 0
               && CliEntry.IsHookVerb(parsed)
               && ReferenceEquals(parsed.Action, parsed.CommandResult.Command.Action);
    }

    private static IEnumerable<string> CommandArguments(string? solution, string? spec)
    {
        if (solution is not null) yield return CommandLineArgument.HookPath(solution);

        if (spec is null) yield break;

        yield return "--spec";
        yield return CommandLineArgument.HookPath(spec);
    }

    // A file holding only whitespace counts as an empty one, as Claude Code reads no setting from it either.
    private static JsonObject Parse(string? existing, string filePath)
    {
        if (string.IsNullOrWhiteSpace(existing)) return [];

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(existing);
        }
        catch (JsonException ex)
        {
            throw new UserErrorException($"{filePath} is not valid JSON: {ex.Message} Fix it, then run this again.", ex);
        }

        return root as JsonObject ?? throw WrongShape(filePath, root, "an object");
    }

    private static int MergeDeny(JsonObject target, JsonArray templateDeny, List<string> lines, string filePath)
    {
        JsonObject permissions = ObjectAt(target, PermissionsKey, filePath);
        JsonArray deny = ArrayAt(permissions, DenyKey, filePath);
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? rule in deny)
            present.Add(StringOf(rule) ?? throw WrongShape(filePath, deny, "an array of strings"));

        var added = 0;
        foreach (string rule in templateDeny.Select(static rule => rule!.GetValue<string>()))
        {
            if (!present.Add(rule)) continue;

            deny.Add(rule);
            lines.Add($"added permissions.deny: {rule}");
            added++;
        }

        return added;
    }

    private static int MergeHooks(
        JsonObject target, JsonObject templateHooks, string[] suffix, Command commandTree, List<string> lines,
        string filePath)
    {
        JsonObject hooks = ObjectAt(target, HooksKey, filePath);
        var added = 0;
        foreach ((string eventName, JsonNode? templateGroups) in templateHooks)
        {
            JsonArray groups = ArrayAt(hooks, eventName, filePath);
            foreach (JsonObject templateGroup in templateGroups!.AsArray()
                         .OfType<JsonObject>())
            {
                string? matcher = Matcher(templateGroup);
                string label = matcher is null ? eventName : $"{eventName} ({matcher})";
                if (PresentRunner(groups, matcher, commandTree) is { } kept)
                {
                    lines.Add($"kept {label}: {kept}");
                    continue;
                }

                JsonObject group = WithSuffix(templateGroup, suffix);
                groups.Add(group);
                lines.AddRange(Commands(group)
                    .Select(command => $"added {label}: {command.Shown}"));
                added++;
            }
        }

        return added;
    }

    // The command of the target group, on the template group's matcher, that runs the hook verb; null when none does.
    // A group or a hook entry of another shape is the user's and is read past rather than refused, since the merge
    // never writes into one.
    private static string? PresentRunner(JsonArray groups, string? matcher, Command commandTree)
    {
        return groups.OfType<JsonObject>()
            .Where(group => IsMatcher(group, matcher))
            .SelectMany(Commands)
            .FirstOrDefault(command => RunsHookRunner(commandTree, command.Tokens))
            ?.Shown;
    }

    private static bool IsMatcher(JsonObject group, string? matcher)
    {
        JsonNode? node = group[MatcherKey];
        if (node is null) return matcher is null;

        return StringOf(node) is { } text && string.Equals(text, matcher, StringComparison.Ordinal);
    }

    private static string? Matcher(JsonObject group)
    {
        return StringOf(group[MatcherKey]);
    }

    // The node's string value; null for a missing node, a JSON null or any non-string.
    private static string? StringOf(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
    }

    private static IEnumerable<HookCommand> Commands(JsonObject group)
    {
        return (group[HooksKey] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(HookCommand.Of)
            .OfType<HookCommand>();
    }

    private static JsonObject WithSuffix(JsonObject templateGroup, string[] suffix)
    {
        var group = (JsonObject)templateGroup.DeepClone();
        foreach (JsonObject hook in (group[HooksKey] as JsonArray ?? []).OfType<JsonObject>())
        {
            JsonArray args = hook[ArgsKey] as JsonArray
                             ?? throw new InvalidOperationException("A hook entry of the settings template has no args array.");
            foreach (string word in suffix) args.Add(word);
        }

        return group;
    }

    private static JsonObject ObjectAt(JsonObject parent, string key, string filePath)
    {
        if (!parent.TryGetPropertyValue(key, out JsonNode? node))
        {
            JsonObject created = [];
            parent[key] = created;
            return created;
        }

        return node as JsonObject ?? throw WrongShape(filePath, node, "an object", parent, key);
    }

    private static JsonArray ArrayAt(JsonObject parent, string key, string filePath)
    {
        if (!parent.TryGetPropertyValue(key, out JsonNode? node))
        {
            JsonArray created = [];
            parent[key] = created;
            return created;
        }

        return node as JsonArray ?? throw WrongShape(filePath, node, "an array", parent, key);
    }

    // A null value has no node to ask its path of, so its path is its parent's and its key.
    private static UserErrorException WrongShape(
        string filePath, JsonNode? node, string expected, JsonObject? parent = null, string? key = null)
    {
        string path = node?.GetPath() ?? (parent is null ? "$" : $"{parent.GetPath()}.{key}");
        return new UserErrorException(
            $"{filePath}: {path} is not {expected}, so the hook settings cannot be merged into it. Fix it, then run "
            + "this again.");
    }

    // An entry's command as Claude Code runs it. Tokens is the argument vector: exec form's command and args as
    // written, or shell form's command line as the splitter reads it. Shown is the line a change reports. An entry of
    // neither shape, or with an args element that is not a string, has none.
    private sealed record HookCommand(string Shown, string[] Tokens)
    {
        public static HookCommand? Of(JsonObject hook)
        {
            if (StringOf(hook[CommandKey]) is not { } command) return null;

            if (!hook.TryGetPropertyValue(ArgsKey, out JsonNode? node))
                return new HookCommand(command, CommandLineParser.SplitCommandLine(command)
                    .ToArray());

            if (node is not JsonArray args) return null;

            var tokens = new List<string> { command };
            foreach (JsonNode? arg in args)
            {
                if (StringOf(arg) is not { } text) return null;

                tokens.Add(text);
            }

            return new HookCommand(string.Join(" ", tokens.Select(CommandLineArgument.Quoted)), tokens.ToArray());
        }
    }

    /// <summary>
    ///     A merge's outcome: the file's text, a line per entry and rule added or kept, and whether anything was added.
    /// </summary>
    internal sealed record Result(string Text, IReadOnlyList<string> Lines, bool Changed);
}
