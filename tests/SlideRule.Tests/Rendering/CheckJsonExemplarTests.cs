using System.Text.Json;
using Shouldly;
using SlideRule.Checking;
using SlideRule.Cli.Rendering;
using SlideRule.Codebase;
using SlideRule.Tests.Checking;
using SlideRule.Tests.Extraction;
using Xunit;
// The suite's reader over a check document, not the CLI renderer of the same name that this file also imports.
using CheckJson = SlideRule.Tests.TestSupport.CheckJson;

namespace SlideRule.Tests.Rendering;

/// <summary>
///     A rule's exemplar on the <c>check --json</c> document (and so on <c>arch_check</c>'s): the types it
///     resolved to, each by full name with every declaration site. Built from source in-process, because no
///     bed's spec names an exemplar.
/// </summary>
public sealed class CheckJsonExemplarTests
{
    private const string Page = "https://learn.microsoft.com/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures";

    // A web type reaching the data layer, so the rule is red, and beside it the two types the fix points at:
    // a service declared once and a repository declared in two partial parts, on lines 7, 5 and 6.
    private const string Source = """
                                  namespace App.Web { public class HomeController { public App.Data.Db Db; } }
                                  namespace App.Data
                                  {
                                      public class Db {}
                                      public partial class OrdersRepository {}
                                      public partial class OrdersRepository { public void Save() {} }
                                      public class OrderService {}
                                  }
                                  """;

    private static readonly CodebaseModel Codebase = CompilationFactory.Extract(Source);

    private static readonly CheckReport Pointed = Report(arch => arch.Types.Named("OrdersRepository", "OrderService"));

    private static readonly CheckReport Dead = Report(arch => arch.Types.Named("GhostRepository"));

    private static readonly CheckReport Unpointed = Checker.Run(Codebase, arch =>
        arch.Rule("layer/no-data")
            .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
            .Because("The web layer must not open the data layer directly.")
            .Citation(Page));

    [Fact]
    public void Rule_WithAResolvedExemplar_CarriesEachTypeByFullNameWithEverySiteInOrdinalOrder()
    {
        // OrderService sorts before OrdersRepository ordinally ('S' is below 's'), whichever order the spec
        // named them in; the partial repository carries both of its declaring parts.
        JsonElement element = Rule(Pointed, DocumentGrain.Full)
            .GetProperty("exemplar");
        // Re-serialized compact, so the pin reads as one line rather than the document's indentation.
        string exemplar = JsonSerializer.Serialize(element);

        exemplar.ShouldBe(
            "[{\"type\":\"App.Data.OrderService\",\"sites\":[{\"file\":\"Test.cs\",\"line\":7}]},"
            + "{\"type\":\"App.Data.OrdersRepository\",\"sites\":[{\"file\":\"Test.cs\",\"line\":5},"
            + "{\"file\":\"Test.cs\",\"line\":6}]}]");
    }

    [Fact]
    public void Rule_WithAnExemplar_WritesItAfterTheCitation()
    {
        // After the prose a reader already scripts against, and before the verdict's counts, so the keys that
        // were there keep their order.
        string[] keys = Rule(Pointed, DocumentGrain.Full)
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        Array.IndexOf(keys, "exemplar")
            .ShouldBe(Array.IndexOf(keys, "citation") + 1, string.Join(", ", keys));
    }

    [Fact]
    public void Rule_WithoutAnExemplar_OmitsTheKey()
    {
        Rule(Unpointed, DocumentGrain.Full)
            .TryGetProperty("exemplar", out _)
            .ShouldBeFalse();
    }

    [Fact]
    public void Rule_WhoseExemplarResolvedNothing_OmitsTheKeyAndWarnsInstead()
    {
        // Absent rather than an empty array: there is nothing to point at, and the dead name is the warning's.
        JsonElement rule = Rule(Dead, DocumentGrain.Full);

        rule.TryGetProperty("exemplar", out _)
            .ShouldBeFalse();
        rule.GetProperty("warnings")
            .EnumerateArray()
            .Single()
            .GetProperty("kind")
            .GetString()
            .ShouldBe("deadExemplar");
    }

    [Fact]
    public void Rule_AtIndexGrain_ElidesTheExemplarWithTheRestOfTheProse()
    {
        JsonElement rule = Rule(Pointed, DocumentGrain.Index);

        rule.TryGetProperty("exemplar", out _)
            .ShouldBeFalse();
        rule.TryGetProperty("fix", out _)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(nameof(DocumentGrain.Overview))]
    [InlineData(nameof(DocumentGrain.Skeleton))]
    public void Rule_AtOverviewAndSkeletonGrain_KeepsTheExemplarAndItsSites(string grainName)
    {
        // By name, because DocumentGrain is internal and a public theory cannot take it as a parameter.
        var grain = Enum.Parse<DocumentGrain>(grainName);

        // Overview elides a violation's sites because they scale with the codebase; an exemplar's do not, and
        // without them the pointer says what to copy but not where it is.
        Rule(Pointed, grain)
            .GetProperty("exemplar")
            .EnumerateArray()
            .Select(type => type.GetProperty("sites").GetArrayLength())
            .ShouldBe([1, 2]);
    }

    [Fact]
    public void Document_CarryingAnExemplar_StaysAtSchemaVersionThree()
    {
        // The key is optional and absent when empty, so a reader of version 3 loses nothing it relied on.
        using JsonDocument document = JsonDocument.Parse(Pointed.JsonReport());

        document.RootElement.GetProperty("schemaVersion")
            .GetInt32()
            .ShouldBe(3);
    }

    private static CheckReport Report(Func<Arch, Selection> exemplar)
    {
        return Checker.Run(Codebase, arch =>
            arch.Rule("layer/no-data")
                .Enforce(arch.Namespace("App.Web.*").MustNotReference(arch.Namespace("App.Data.*")))
                .Because("The web layer must not open the data layer directly.")
                .Fix("Go through a repository.")
                .Citation(Page)
                .Exemplar(exemplar(arch)));
    }

    private static JsonElement Rule(CheckReport report, DocumentGrain grain)
    {
        return CheckJson.Rule(report.JsonReport(grain), "layer/no-data");
    }
}
