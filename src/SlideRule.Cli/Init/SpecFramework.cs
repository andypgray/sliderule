using System.Globalization;
using SlideRule.Roslyn;
using SlideRule.Roslyn.Solutions;

namespace SlideRule.Cli.Init;

/// <summary>
///     The target framework <c>init</c> writes into the spec project when <c>--framework</c> is not given, decided
///     from what the product projects target and the SDK a <c>global.json</c> pins.
/// </summary>
/// <remarks>
///     The spec has to build with the SDK the repository pins, the running tool has to load it, and a later
///     <c>typeof()</c> anchor needs it able to reference the product projects. An estate on .NET Framework alone
///     gets its highest framework. Any other estate gets the newest .NET major its projects target, at least 10
///     and at most both the running runtime's major and the pinned SDK's, carrying the one platform suffix the
///     projects at or below that major share, at its highest version (<c>net11.0-windows</c>), since a bare
///     framework cannot reference a platform one. Under a pin below SDK 10 it gets the pinned SDK's own
///     <c>netN.0</c>, which that SDK can build and which references every older target. Below SDK 6 the spec class
///     cannot compile (it needs C# 10), so that pin refuses.
/// </remarks>
internal static class SpecFramework
{
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
    /// <param name="runtimeMajor">The major version of the runtime the tool is running on.</param>
    /// <exception cref="UserErrorException">The pin is below SDK 6.</exception>
    internal static string Choose(
        IReadOnlyCollection<string> productFrameworks,
        int? sdkPinMajor,
        string? globalJson,
        int runtimeMajor)
    {
        List<string> classified = productFrameworks.Where(framework => !IsFramework(framework))
            .ToList();
        if (productFrameworks.Count > 0 && classified.Count == 0)
            return productFrameworks.OrderByDescending(FrameworkVersion)
                .First();

        if (sdkPinMajor is not { } pin || pin >= DefaultMajor)
            return Modern(classified, Math.Min(runtimeMajor, sdkPinMajor ?? int.MaxValue));

        if (pin < MinimumSdkMajor)
            throw new UserErrorException(
                $"{globalJson} pins SDK {pin}, which cannot compile a spec class (it needs C# 10, SDK 6 or later). "
                + "Pass --framework with a framework your SDK can build, or run init outside the pin.");

        return string.Create(CultureInfo.InvariantCulture, $"net{pin}.0");
    }

    // The newest major at or below the ceiling, raised to 10, with a platform suffix when every platform framework
    // at or below that major names the same platform: a platform spec references both its platform's projects and
    // the bare ones, and a bare spec references no platform project (NU1201). Of that platform's suffixes the one
    // with the highest version wins, since it references the lower ones.
    private static string Modern(IEnumerable<string> frameworks, int ceiling)
    {
        List<(int Major, string Platform)> modern = frameworks.Select(ModernParts)
            .OfType<(int Major, string Platform)>()
            .ToList();
        int major = modern.Select(parts => parts.Major)
            .Where(candidate => candidate <= ceiling)
            .Append(DefaultMajor)
            .Max();
        List<IGrouping<string, string>> platforms = modern
            .Where(parts => parts.Major <= major && parts.Platform.Length > 0)
            .Select(parts => parts.Platform)
            .GroupBy(PlatformName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        string platform = platforms.Count == 1 ? platforms[0].MaxBy(PlatformVersion)! : "";

        return string.Create(CultureInfo.InvariantCulture, $"net{major}.0{platform}");
    }

    // "-windows10.0.19041.0" names "windows".
    private static string PlatformName(string platform)
    {
        return new string(platform.Skip(1)
            .TakeWhile(char.IsAsciiLetter)
            .ToArray());
    }

    // "-windows10.0.19041.0" is 10.0.19041.0; a suffix with no version, or one that does not parse, is the lowest.
    private static Version PlatformVersion(string platform)
    {
        string digits = platform[(1 + PlatformName(platform).Length)..];
        return Version.TryParse(digits, out Version? version) ? version : new Version(0, 0);
    }

    // net5.0 and later as their major and platform suffix ("-windows10.0.19041.0", or ""), or null for any other
    // framework.
    private static (int Major, string Platform)? ModernParts(string framework)
    {
        if (!framework.StartsWith("net", StringComparison.Ordinal)) return null;

        int dot = framework.IndexOf('.', StringComparison.Ordinal);
        if (dot < 0) return null;

        string digits = framework[3..dot];
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int major) || major < 5)
            return null;

        int dash = framework.IndexOf('-', StringComparison.Ordinal);
        string platform = dash < 0 ? "" : framework[dash..];
        return (major, platform);
    }

    /// <summary>
    ///     The major version of the SDK the nearest <c>global.json</c> at or above <paramref name="directory" />
    ///     pins, and that file, or nulls when none pins one.
    /// </summary>
    internal static (int? Major, string? File) SdkPin(string directory)
    {
        if (GlobalJsonPin.Nearest(directory) is not { } pin) return (null, null);

        return (SdkMajor(pin), pin.File);
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
    // malformed global.json, and that refusal names the file better than one here would. Under the two
    // cross-major rollForward policies the pinned version is a floor, not a ceiling: a newer installed SDK builds
    // it, so there is no major to honour.
    private static int? SdkMajor(GlobalJsonPin pin)
    {
        if (pin.Version is not { } version || pin.RollForward is "major" or "latestMajor") return null;

        string major = version.Split('.')[0];
        return int.TryParse(major, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;
    }
}
