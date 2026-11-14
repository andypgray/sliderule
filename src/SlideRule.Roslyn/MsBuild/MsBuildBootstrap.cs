using Microsoft.Build.Locator;
using SlideRule.Roslyn.Diagnostics;
using SlideRule.Roslyn.Hosting;

namespace SlideRule.Roslyn.MsBuild;

/// <summary>
///     Chooses the MSBuild a host loads a solution with, and registers it both for the running process and for the
///     separate build-host process Roslyn starts to read project files.
///     On Windows the build-host process needs a Visual Studio install to load a project that is not SDK-style, so
///     one is used when present, preferring Visual Studio 2019, 2022 or 2026 over any other version. To choose a
///     different one, set the <c>SLIDERULE_VS_INSTALL_PATH</c> environment variable to a Visual Studio install
///     root (the parent of <c>MSBuild\Current\Bin</c>); a value that is not an existing directory, or holds no
///     <c>MSBuild.exe</c> there, fails with a message naming the variable and the path that was probed.
/// </summary>
// A host inside this repository calls EnsureInitialized before anything touches a Roslyn
// workspace type: left until later, the runtime resolves MSBuild assemblies before anything has said which
// ones to resolve. The two processes are pointed at MSBuild separately, and not at the same one: see
// ApplyDevConsoleEnv for the BuildHost subprocess and RegisterEngineForThisProcess for this one.
// ApplyVsToolsPath points both at the selected install's targets.
internal static class MsBuildBootstrap
{
    private const string DevConsoleVersion = "99.0";

    // The layout every Visual Studio install puts MSBuild under — quoted by the guidance sentences and
    // probed by TryRegisterFromVsRoot, so the directory a reader is sent to is the directory the code
    // probed. Windows separators: every VS install root is a Windows path.
    private const string MsBuildBinLayout = @"MSBuild\Current\Bin";

    private const string VsTargetsLayout = @"MSBuild\Microsoft\VisualStudio";

    // The variables the bootstrap sets process-wide for the build host, which a child dotnet must not inherit.
    internal const string VsInstallDirVariable = "VSINSTALLDIR";
    internal const string VsCmdVerVariable = "VSCMD_VER";
    internal const string VsToolsPathVariable = "VSToolsPath";

    // The tail an instance taken outside the preferred VS 16/17/18 set carries in its description. The
    // preferred arm needs no counterpart: an ordinary pick is the unremarkable case, and a note on every
    // line would stop the exceptional one from reading as exceptional.
    private const string OutsideEnvelopeNote = " — outside the tested VS 2019/2022/2026 envelope";

    /// <summary>
    ///     The <see cref="MsBuildSelection.Source" /> of the most recent registration; <see langword="null" /> until
    ///     something registers.
    /// </summary>
    /// <remarks>
    ///     A <see langword="string" /> rather than the <see cref="MsBuildSelection" /> itself, deliberately:
    ///     this namespace is quarantined behind <see cref="MsBuildBootstrap" />, so a caller outside it may
    ///     name this type and nothing else here.
    /// </remarks>
    internal static string? LastSelection { get; private set; }

    /// <summary>
    ///     The MSBuild-selection line that rides along with workspace diagnostics: a project that fails to load
    ///     is nearly always a question about which MSBuild opened it. A null selection means nothing registered
    ///     MSBuild at all, which for a caller that just opened a workspace is itself worth saying. The override
    ///     sentence rides only on Windows, the one host with a Visual Studio install root to name.
    /// </summary>
    internal static string SelectionNote()
    {
        return ComposeSelectionNote(LastSelection, OperatingSystem.IsWindows());
    }

    /// <summary>
    ///     The note <see cref="SelectionNote" /> prints, for a given selection and host.
    /// </summary>
    // A deliberate seam: pure, like DescribeDefaultFallback, so a test pins the Windows and non-Windows notes on
    // any machine.
    internal static string ComposeSelectionNote(string? selection, bool offersVsOverride)
    {
        var note = $"MSBuild for this run: {selection ?? "not registered by this process"}.";
        if (!offersVsOverride) return note;

        return $"{note} Set {SlideRuleEnvVars.VsInstallPath} to a Visual Studio install root (the parent of "
               + $"{MsBuildBinLayout}) to select a different MSBuild.";
    }

    /// <summary>
    ///     Selects an MSBuild instance, registers an engine for this process, and points the build-host process at the
    ///     Visual Studio install behind the selection. Returns what was chosen, whose
    ///     <see cref="MsBuildSelection.Source" /> is the one line worth logging.
    /// </summary>
    // A deliberate test seam: unlike EnsureInitialized, it selects whether or not MSBuild is already registered,
    // so a test can drive the override's refusals in a process that registered before any test ran.
    internal static MsBuildSelection Initialize()
    {
        return SelectAndRegister();
    }

    /// <summary>
    ///     Registers MSBuild if nothing has yet and returns what was chosen, or returns <see langword="null" /> when
    ///     MSBuild is already registered.
    /// </summary>
    /// <remarks>
    ///     Safe to call from every entry point of a host. A null answer is not a failure: it says the choice was
    ///     already made, and <see cref="MsBuildSelection.Source" /> is only available from the call that made it.
    /// </remarks>
    internal static MsBuildSelection? EnsureInitialized()
    {
        return MSBuildLocator.IsRegistered ? null : SelectAndRegister();
    }

    /// <summary>
    ///     Picks the Visual Studio instance to register MSBuild from: the highest of the preferred majors
    ///     (16 = VS 2019, 17 = VS 2022, 18 = VS 2026), or the highest of any major when none of those is
    ///     installed.
    /// </summary>
    /// <remarks>
    ///     The preference is caution about a moving target, and no known break motivates it: these are the
    ///     tested majors, which load both legacy <c>http://schemas.microsoft.com/developer/msbuild/2003</c>
    ///     projects and SDK-style ones. <see cref="DescribeSelection" /> says so for a pick outside them.
    /// </remarks>
    internal static VsInstance? SelectBestInstance(IReadOnlyList<VsInstance> instances)
    {
        if (instances.Count == 0) return null;

        VsInstance? stable = instances
            .Where(IsPreferredMajor)
            .OrderByDescending(i => i.Version)
            .FirstOrDefault();

        return stable ?? instances.OrderByDescending(i => i.Version).First();
    }

    /// <summary>
    ///     The one-line description of a chosen instance, naming the version and, when the instance came from
    ///     outside the preferred set, that it did. Pure, the reporting half of
    ///     <see cref="RegisterFromVsInstance" />, so it can be pinned on a machine with no Visual Studio installed.
    /// </summary>
    internal static string DescribeSelection(VsInstance instance)
    {
        string envelope = IsPreferredMajor(instance) ? string.Empty : OutsideEnvelopeNote;
        return $"{instance.Name} ({instance.Version.Major}.{instance.Version.Minor}, via vswhere){envelope}";
    }

    // The tested envelope: VS 2019, VS 2022 and VS 2026. Shared by the selection and its description so the
    // two cannot drift into describing an instance as preferred that the selection took as a fallback.
    // Must agree with OutsideEnvelopeNote's wording and with ci.yml's per-image selection assertion.
    private static bool IsPreferredMajor(VsInstance instance)
    {
        return instance.Version.Major is 16 or 17 or 18;
    }

    // Registration, plus the publication of what it chose. Every arm below returns a selection, so
    // recording it once here is what keeps LastSelection total rather than a thing each arm remembers.
    private static MsBuildSelection SelectAndRegister()
    {
        MsBuildSelection selection = SelectAndRegisterCore();
        LastSelection = selection.Source;
        return selection;
    }

    private static MsBuildSelection SelectAndRegisterCore()
    {
        string? overridePath = Environment.GetEnvironmentVariable(SlideRuleEnvVars.VsInstallPath);
        if (!string.IsNullOrWhiteSpace(overridePath)) return RegisterFromOverride(overridePath);

        string? whyNone = null;
        if (OperatingSystem.IsWindows())
        {
            (IReadOnlyList<VsInstance> instances, whyNone) = VsWhereLocator.Query();
            VsInstance? best = SelectBestInstance(instances);
            if (best is not null) return RegisterFromVsInstance(best);
        }

        RegisterDefaults();
        return new MsBuildSelection(DescribeDefaultFallback(whyNone));
    }

    /// <summary>
    ///     The selection reported when no Visual Studio was taken, carrying vswhere's reason when there is one.
    ///     Pure, like <see cref="DescribeSelection" />, so it can be pinned on any machine.
    /// </summary>
    // A null reason means vswhere was never asked, which is every non-Windows host: on Windows each answer that
    // lists no instance carries a reason. ci.yml's MSBuild assertion matches the null form exactly on its
    // non-Windows legs.
    internal static string DescribeDefaultFallback(string? whyNone)
    {
        return whyNone is null
            ? "MSBuildLocator default (the .NET SDK's MSBuild)"
            : $"MSBuildLocator default (no Visual Studio install detected via vswhere; {whyNone})";
    }

    private static MsBuildSelection RegisterFromOverride(string overridePath)
    {
        string vsRoot = overridePath.TrimEnd('\\', '/');
        if (!Directory.Exists(vsRoot))
            throw new InvalidOperationException(
                $"{SlideRuleEnvVars.VsInstallPath}='{overridePath}' is not an existing directory. " +
                "Set it to the VS install root (e.g. " +
                "'C:\\Program Files\\Microsoft Visual Studio\\2022\\Community').");

        (_, string msBuildExe, bool registered) = TryRegisterFromVsRoot(vsRoot);
        if (!registered)
            throw new InvalidOperationException(
                $"{SlideRuleEnvVars.VsInstallPath}='{overridePath}': MSBuild.exe not found at '{msBuildExe}'. " +
                $"Set the env var to the VS install root (the parent of '{MsBuildBinLayout}').");

        return new MsBuildSelection($"{SlideRuleEnvVars.VsInstallPath} override ('{vsRoot}')");
    }

    private static MsBuildSelection RegisterFromVsInstance(VsInstance instance)
    {
        (string msBuildBin, _, bool registered) = TryRegisterFromVsRoot(instance.InstallationPath);
        if (!registered)
        {
            // VS install missing MSBuild — extremely unusual but degrade gracefully.
            RegisterDefaults();
            return new MsBuildSelection(
                $"MSBuildLocator default (selected '{instance.Name}' had no MSBuild at '{msBuildBin}')");
        }

        return new MsBuildSelection(DescribeSelection(instance));
    }

    // A missed probe registers nothing and hands both probed paths back for the failure to name: an operator
    // who named this root has to be told it was wrong, while a VS install picked here degrades to
    // MSBuildLocator's defaults rather than failing a run over it.
    private static (string MsBuildBin, string MsBuildExe, bool Registered) TryRegisterFromVsRoot(string vsRoot)
    {
        string msBuildBin = Path.Combine(vsRoot, MsBuildBinLayout);
        string msBuildExe = Path.Combine(msBuildBin, "MSBuild.exe");
        if (!File.Exists(msBuildExe)) return (msBuildBin, msBuildExe, false);

        ApplyDevConsoleEnv(vsRoot);
        ApplyVsToolsPath(vsRoot);
        RegisterEngineForThisProcess(msBuildBin);

        return (msBuildBin, msBuildExe, true);
    }

    // Which MSBuild the BuildHost opens projects with and which one runs inside this process are two choices,
    // and only the first is about Visual Studio (ApplyDevConsoleEnv). In-process the engine has to be a .NET
    // build of MSBuild or it will not run at all: Visual Studio ships a .NET Framework one, whose SDK resolver
    // cannot be loaded here, and this process evaluates project files directly. So the .NET SDK's MSBuild is
    // registered, as on every non-Windows run. A machine with no SDK at all falls back to the VS path: nothing
    // here can then evaluate, and an unevaluated project records no artifact facts, which the model already
    // knows how to be missing.
    private static void RegisterEngineForThisProcess(string msBuildBin)
    {
        try
        {
            MSBuildLocator.RegisterDefaults();
        }
        catch (InvalidOperationException)
        {
            MSBuildLocator.RegisterMSBuildPath(msBuildBin);
        }
    }

    // The default registration resolves the SDK from the working directory, so a global.json there pinning an
    // SDK the host cannot resolve fails it; that failure is worded here, and anything else passes through.
    private static void RegisterDefaults()
    {
        try
        {
            MSBuildLocator.RegisterDefaults();
        }
        catch (InvalidOperationException ex) when (SdkResolutionRefusal.DetailOf(ex) is not null)
        {
            throw SdkResolutionRefusal.ForRegistration(Environment.CurrentDirectory, ex);
        }
    }

    /// <summary>
    ///     Sets the env vars that the Roslyn BuildHost subprocess inherits: its MSBuildLocator honours
    ///     VSINSTALLDIR and VSCMD_VER as a synthetic "developer console" instance pointing at the selected VS,
    ///     and VSCMD_VER=99.0 wins its descending-version sort against any other installed VS.
    /// </summary>
    /// <remarks>
    ///     VisualStudioVersion must not be used for this: MSBuild itself reads that variable during project
    ///     evaluation.
    /// </remarks>
    private static void ApplyDevConsoleEnv(string vsRoot)
    {
        Environment.SetEnvironmentVariable(VsInstallDirVariable, vsRoot);
        Environment.SetEnvironmentVariable(VsCmdVerVariable, DevConsoleVersion);
    }

    // The .NET SDK's MSBuild evaluates projects that are not SDK-style too: the .NET build host does, as the
    // ProjectReference of an SDK-style project's design-time build, and so does this process, for artifact facts.
    // Its MSBuildExtensionsPath32 is the SDK directory, so the VSToolsPath default such a project writes there
    // names no Visual Studio targets (MSB4019 for a web application), and a value in the environment pre-empts
    // that default. The build host inherits it, so it names the selected install's targets; a value already in
    // the environment is the user's and is kept.
    private static void ApplyVsToolsPath(string vsRoot)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(VsToolsPathVariable))) return;

        string? vsToolsPath = VsToolsPathUnder(vsRoot);
        if (vsToolsPath is not null) Environment.SetEnvironmentVariable(VsToolsPathVariable, vsToolsPath);
    }

    // Visual Studio keeps its targets under MSBuild\Microsoft\VisualStudio\v<major>.0 of its install root, the
    // directory its own MSBuild's VSToolsPath names; the highest version present is that install's own.
    private static string? VsToolsPathUnder(string vsRoot)
    {
        string extensions = Path.Combine(vsRoot, VsTargetsLayout);
        if (!Directory.Exists(extensions)) return null;

        return Directory.EnumerateDirectories(extensions, "v*")
            .Select(directory => (Directory: directory, Version: VersionOf(directory)))
            .Where(candidate => candidate.Version is not null)
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => candidate.Directory)
            .FirstOrDefault();
    }

    private static Version? VersionOf(string directory)
    {
        string name = Path.GetFileName(directory);
        return Version.TryParse(name.Substring(1), out Version? version) ? version : null;
    }
}
