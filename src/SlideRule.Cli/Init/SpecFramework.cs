using System.Globalization;
using System.Text.Json;
using SlideRule.Roslyn;

namespace SlideRule.Cli.Init;

/// <summary>
///     The target framework <c>init</c> writes into the spec project when <c>--framework</c> is not given, decided
///     from what the product projects target and the SDK a <c>global.json</c> pins.
/// </summary>
/// <remarks>
///     The spec has to build with the SDK the repository pins, and a later <c>typeof()</c> anchor needs it able to
///     reference the product projects. An estate on .NET Framework alone gets its highest framework; any other
///     estate gets <c>net10.0</c>, or under a pin below SDK 10 the pinned SDK's own <c>netN.0</c>, which that SDK can
///     build and which references every older target. Below SDK 6 the spec class cannot compile (it needs C# 10),
///     so that pin refuses.
/// </remarks>
internal static class SpecFramework
{
    private const string Default = "net10.0";

    private const int DefaultMajor = 10;

    // The oldest SDK whose compiler takes the spec class: file-scoped namespaces are C# 10.
    private const int MinimumSdkMajor = 6;

    /// <summary>
    ///     The framework for a spec over <paramref name="productFrameworks" /> under the SDK pin, if any.
    /// </summary>
    /// <param name="productFrameworks">
    ///     Every target framework the product projects declare, as the model records them.
    /// </param>
    /// <param name="sdkPinMajor">
    ///     The major version of the SDK <c>global.json</c> pins, or null when nothing pins one.
    /// </param>
    /// <param name="globalJson">The pinning file, named in the refusal.</param>
    /// <exception cref="UserErrorException">The pin is below SDK 6.</exception>
    internal static string Choose(IReadOnlyCollection<string> productFrameworks, int? sdkPinMajor, string? globalJson)
    {
        List<string> classified = productFrameworks.Where(framework => !IsFramework(framework))
            .ToList();
        if (productFrameworks.Count > 0 && classified.Count == 0)
            return productFrameworks.OrderByDescending(FrameworkVersion)
                .First();

        if (sdkPinMajor is not { } pin || pin >= DefaultMajor) return Default;

        if (pin < MinimumSdkMajor)
            throw new UserErrorException(
                $"{globalJson} pins SDK {pin}, which cannot compile a spec class (it needs C# 10, SDK 6 or later). "
                + "Pass --framework with a framework your SDK can build, or run init outside the pin.");

        return string.Create(CultureInfo.InvariantCulture, $"net{pin}.0");
    }

    /// <summary>
    ///     The major version of the SDK the nearest <c>global.json</c> at or above <paramref name="directory" />
    ///     pins, and that file, or nulls when none pins one.
    /// </summary>
    internal static (int? Major, string? File) SdkPin(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            string candidate = Path.Combine(current.FullName, "global.json");
            if (!File.Exists(candidate)) continue;

            return (SdkMajor(File.ReadAllText(candidate)), candidate);
        }

        return (null, null);
    }

    // net48, net472, net35: .NET Framework's short monikers are "net" and two or three digits, with no dot.
    // netstandard and netcoreapp do not match. A generated regex would emit helper types that SlideRule.Roslyn's
    // own generated regexes already declare, and the self-spec's survey reports that as a duplicate.
    private static bool IsFramework(string framework)
    {
        string digits = framework.StartsWith("net", StringComparison.Ordinal) ? framework[3..] : "";
        return digits.Length is 2 or 3 && digits.All(char.IsAsciiDigit);
    }

    // The digits as a three-digit version, so net48 (4.8) outranks net472 (4.7.2).
    private static int FrameworkVersion(string framework)
    {
        string digits = framework[3..].PadRight(3, '0');
        return int.Parse(digits, CultureInfo.InvariantCulture);
    }

    // The major of sdk.version, or null where the file names none or does not parse: the SDK itself refuses a
    // malformed global.json, and that refusal names the file better than one here would.
    private static int? SdkMajor(string globalJson)
    {
        JsonDocumentOptions options = new()
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        try
        {
            using JsonDocument document = JsonDocument.Parse(globalJson, options);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sdk", out JsonElement sdk)
                || sdk.ValueKind != JsonValueKind.Object
                || !sdk.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.String
                || RollsForwardAcrossMajors(sdk))
                return null;

            string major = version.GetString()!.Split('.')[0];
            return int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Under these two policies the pinned version is a floor, not a ceiling: a newer installed SDK builds it.
    private static bool RollsForwardAcrossMajors(JsonElement sdk)
    {
        return sdk.TryGetProperty("rollForward", out JsonElement rollForward)
               && rollForward.ValueKind == JsonValueKind.String
               && rollForward.GetString() is "major" or "latestMajor";
    }
}
