using Microsoft.CodeAnalysis;

namespace SlideRule.Tests.TestSupport;

/// <summary>
///     Analyzer assemblies the carried compiler cannot load, compiled per test into its temp directory: one built against
///     a stand-in <c>Microsoft.CodeAnalysis</c> at version 99.0.0.0, and one whose generator's base class sits in an
///     assembly left out of the directory.
/// </summary>
internal static class StubAnalyzers
{
    /// <summary>The file name of the compiler assembly a yardstick reads.</summary>
    internal const string CompilerAssemblyName = "Microsoft.CodeAnalysis.CSharp.dll";

    private const string NewerCompilerContract = """
                                                 [assembly: System.Reflection.AssemblyVersion("99.0.0.0")]
                                                 namespace Microsoft.CodeAnalysis
                                                 {
                                                     [System.AttributeUsage(System.AttributeTargets.Class)]
                                                     public sealed class GeneratorAttribute : System.Attribute { }
                                                     public interface IIncrementalGenerator { }
                                                 }
                                                 namespace Microsoft.CodeAnalysis.Diagnostics
                                                 {
                                                     [System.AttributeUsage(System.AttributeTargets.Class)]
                                                     public sealed class DiagnosticAnalyzerAttribute : System.Attribute
                                                     {
                                                         public DiagnosticAnalyzerAttribute(string firstLanguage, params string[] additionalLanguages) { }
                                                     }
                                                     public abstract class DiagnosticAnalyzer { }
                                                 }
                                                 """;

    /// <summary>
    ///     The path segments, under a dotnet root, at which an installed SDK of <paramref name="sdkVersion" /> keeps its
    ///     compiler (<see cref="SdkCompilerSegments" />) and an SDK analyzer (<see cref="SdkAnalyzerSegments" />).
    /// </summary>
    internal static string[] SdkCompilerSegments(string sdkVersion)
    {
        return ["sdk", sdkVersion, "Roslyn", "bincore", CompilerAssemblyName];
    }

    /// <inheritdoc cref="SdkCompilerSegments" />
    internal static string[] SdkAnalyzerSegments(string sdkVersion)
    {
        return ["sdk", sdkVersion, "Sdks", "Fake.Sdk", "analyzers", "Fake.Analyzer.dll"];
    }

    internal static string NewerCompilerGenerator(TempDirectory temp)
    {
        return Emit(
            temp, "Gen99",
            "[Microsoft.CodeAnalysis.Generator] public sealed class G99 : Microsoft.CodeAnalysis.IIncrementalGenerator { }",
            NewerContract());
    }

    internal static string NewerCompilerAnalyzer(TempDirectory temp)
    {
        return Emit(
            temp, "Ana99",
            "[Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer(\"C#\")] "
            + "public sealed class A99 : Microsoft.CodeAnalysis.Diagnostics.DiagnosticAnalyzer { }",
            NewerContract());
    }

    internal static string GeneratorMissingItsDependency(TempDirectory temp)
    {
        MetadataReference compiler = MetadataReference.CreateFromFile(typeof(Compilation).Assembly.Location);
        byte[] baseImage = SpecAssemblyCompiler.EmitImage(
            "public abstract class BaseGen : Microsoft.CodeAnalysis.IIncrementalGenerator "
            + "{ public void Initialize(Microsoft.CodeAnalysis.IncrementalGeneratorInitializationContext c) { } }",
            "GenDepBase", WithoutCarriedCompiler(compiler));
        MetadataReference baseReference = MetadataReference.CreateFromImage(baseImage);
        return Emit(
            temp, "GenDep", "[Microsoft.CodeAnalysis.Generator] public sealed class GD : BaseGen { }",
            compiler, baseReference);
    }

    internal static string EmptyAssembly(string path)
    {
        byte[] image = SpecAssemblyCompiler.EmitImage(
            "public static class Empty { }", "Fake.Analyzer", WithoutCarriedCompiler());
        return Write(path, image);
    }

    // A compiler assembly whose LanguageVersion enum declares C# up to highestMajor, the one fact a yardstick reads.
    internal static string CompilerDeclaring(string path, int highestMajor)
    {
        var source =
            $"namespace Microsoft.CodeAnalysis.CSharp {{ public enum LanguageVersion {{ CSharp{highestMajor} = {highestMajor * 100} }} }}";
        byte[] image = SpecAssemblyCompiler.EmitImage(source, "Microsoft.CodeAnalysis.CSharp", WithoutCarriedCompiler());
        return Write(path, image);
    }

    // A directory a build's CscToolPath can name: an executable that starts and fails, so the build records its call
    // and compiles nothing, beside a compiler assembly declaring C# up to highestMajor.
    internal static string StandInCompiler(TempDirectory temp, int highestMajor, string name = "compiler")
    {
        string directory = temp.PathOf(name);
        Directory.CreateDirectory(directory);
        File.Copy(Environment.ProcessPath!, Path.Combine(directory, "csc.exe"));
        CompilerDeclaring(Path.Combine(directory, CompilerAssemblyName), highestMajor);
        return directory;
    }

    // A directory a project's RoslynTargetsPath can name, as a compiler package's is: the build targets and tasks of the
    // SDK compiler under sdkRoslynDirectory, beside a compiler assembly declaring C# up to highestMajor in place of that
    // SDK's.
    internal static string CompilerPackage(TempDirectory temp, string sdkRoslynDirectory, int highestMajor)
    {
        string directory = temp.PathOf("compiler-package");
        Directory.CreateDirectory(directory);
        IEnumerable<string> buildFiles = Directory.EnumerateFiles(sdkRoslynDirectory)
            .Where(file => Path.GetExtension(file) is ".targets" || Path.GetFileName(file).StartsWith(
                "Microsoft.Build.Tasks.CodeAnalysis.", StringComparison.Ordinal));
        foreach (string file in buildFiles) File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));

        CompilerDeclaring(Path.Combine(directory, CompilerAssemblyName), highestMajor);
        return directory;
    }

    private static string Write(string path, byte[] image)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, image);
        return path;
    }

    private static MetadataReference NewerContract()
    {
        byte[] image = SpecAssemblyCompiler.EmitImage(
            NewerCompilerContract, "Microsoft.CodeAnalysis", WithoutCarriedCompiler());
        return MetadataReference.CreateFromImage(image);
    }

    private static string Emit(TempDirectory temp, string name, string source, params MetadataReference[] references)
    {
        byte[] image = SpecAssemblyCompiler.EmitImage(source, name, WithoutCarriedCompiler(references));
        return Write(temp.PathOf("analyzers", $"{name}.dll"), image);
    }

    // The platform closure without the carried compiler, so a stand-in contract never meets the real one.
    private static IReadOnlyList<MetadataReference> WithoutCarriedCompiler(params MetadataReference[] references)
    {
        IEnumerable<MetadataReference> platform = SpecAssemblyCompiler.PlatformReferences
            .Where(reference => !Path.GetFileName(reference.Display ?? "").StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal));
        return [.. platform, .. references];
    }
}
