namespace SlideRule.Tests.DocHygiene.Walkthroughs;

/// <summary>
///     Every edit the example walkthroughs ask their reader to imagine, each written out as the change it makes
///     to the committed tree. The replay makes each one in memory and holds the pages' output fences to what the
///     edited tree prints; the gates that need no build hold each anchor to the tree and each edit fence to its
///     scenario.
/// </summary>
/// <remarks>
///     The imagined files' paths are named here once, because the anchor gate names them too: their output is
///     quoted on the pages with anchors into files the tree must never hold.
/// </remarks>
internal static class WalkthroughScenarios
{
    internal const string Meridian = "Meridian";
    private const string Quoting = "Meridian.Quoting";
    internal const string Operations = "Meridian.Operations";
    internal const string Interchange = "Meridian.Interchange";

    /// <summary>The handler the layering walkthrough drops into the Quoting API project.</summary>
    internal const string ExpireQuotesHandlerPath = "src/Meridian.Quoting.Api/Handlers/ExpireQuotesHandler.cs";

    /// <summary>The preview the module-isolation walkthrough adds to Dispatch.</summary>
    internal const string InvoicePreviewPath = "src/Meridian.Operations/Dispatch/InvoicePreview.cs";

    private const string BookingsController = "src/Meridian.Web/Controllers/BookingsController.cs";
    private const string ContainerNumberValidator = "src/Meridian.Clearance/ContainerNumberValidator.cs";
    private const string RequestQuoteHandler = "src/Meridian.Quoting.Application/Handlers/RequestQuoteHandler.cs";
    private const string CarrierClient = "src/Meridian.Interchange/Partners/CarrierClient.cs";
    private const string CustomsFilingClient = "src/Meridian.Interchange/Partners/CustomsFilingClient.cs";
    private const string TerminalClient = "src/Meridian.Interchange/Partners/TerminalClient.cs";
    private const string LegacyManifestClient = "src/Meridian.Interchange/Partners/LegacyManifestClient.cs";
    private const string PartnerClient = "src/Meridian.Interchange/Partners/IPartnerClient.cs";
    private const string OutboxDispatcher = "src/Meridian.Interchange/Dispatch/OutboxDispatcher.cs";
    private const string OutboxProcessor = "src/Meridian.Interchange/Processing/OutboxProcessor.cs";
    private const string OutboxProcessorPort = "src/Meridian.Interchange/Processing/IOutboxProcessor.cs";
    private const string ScopedDispatchRunner = "src/Meridian.Interchange/Host/ScopedDispatchRunner.cs";
    private const string OutboxMessage = "src/Meridian.Interchange/Outbox/OutboxMessage.cs";

    // The end of BookingsController's last action, which both appended methods go in after.
    private const string SampleActionEnd = "        return Ok(sample);\n    }\n";

    // The statistical prior: one inline-SQL method in a migrated controller. OpenAsync and ExecuteScalarAsync
    // bind to the base-class overloads, so the two SqlClient sites are the construction lines alone.
    private const string InlineSqlMethod =
        """

            [HttpGet("{reference}/customer")]
            public async Task<IActionResult> GetCustomerAsync(string reference)
            {
                const string sql = "SELECT CustomerName FROM Bookings WHERE Reference = @reference";
                using var connection = new SqlConnection("Server=.;Database=Meridian;Integrated Security=true");
                using var command = new SqlCommand(sql, connection) { Parameters = { new SqlParameter("@reference", reference) } };
                await connection.OpenAsync();
                object? customer = await command.ExecuteScalarAsync();
                if (customer is null) return NotFound();

                return Ok(customer);
            }

        """;

    private const string GatewayBypassMethod =
        """

            [HttpGet("container-check/{number}")]
            public IActionResult CheckContainer(string number)
            {
                return Ok(new ContainerNumberValidator().IsValid(number));
            }

        """;

    // The lookup the captured agent turn wrote, above the sample endpoint, with the blank line that follows it.
    private const string LookupMethod =
        """
            [HttpGet("{reference}")]
            public async Task<IActionResult> GetByReference(string reference)
            {
                Booking? booking = await bookings.Get(reference);
                if (booking is null) return NotFound();

                return Ok(booking);
            }


        """;

    private const string ExpireQuotesHandlerSource =
        """
        using Meridian.Quoting.Domain;
        using Meridian.Quoting.Infrastructure.Persistence;

        namespace Meridian.Quoting.Application.Handlers;

        /// <summary>
        ///     Reports whether a quote's validity window has closed, reading the quote straight out of the
        ///     in-memory store rather than through the quote repository port.
        /// </summary>
        public sealed class ExpireQuotesHandler(IClock clock)
        {
            private readonly InMemoryDatabase database = new();

            public async Task<bool> IsExpiredAsync(string reference)
            {
                var repository = new InMemoryQuoteRepository(database);
                Quote? quote = await repository.Get(reference);

                return quote is not null && quote.ExpiresUtc <= clock.UtcNow;
            }
        }

        """;

    private const string InvoicePreviewSource =
        """
        using Meridian.Operations.Invoicing;
        using Meridian.Operations.Tracking.Contracts;

        namespace Meridian.Operations.Dispatch;

        /// <summary>A quick invoice preview for the dispatcher, built straight from Invoicing's assembler.</summary>
        internal sealed class InvoicePreview(ITrackingLog tracking)
        {
            public string For(string shipmentId) => $"{new InvoiceAssembler().Assemble(shipmentId, tracking.MilestonesFor(shipmentId), shipmentId, 0m)}";
        }

        """;

    private const string DeliveryLoopBody =
        """
                {
                    IPartnerClient partner = partners.First(client => client.Channel == message.Channel);
                    bool delivered = await TryDeliverAsync(partner, message, maxAttempts, cancellationToken);
                    if (delivered) await store.MarkSentAsync(message.MessageId, cancellationToken);
                }
        """;

    private const string SwallowingDeliveryLoopBody =
        """
                {
                    try
                    {
                        IPartnerClient partner = partners.First(client => client.Channel == message.Channel);
                        bool delivered = await TryDeliverAsync(partner, message, maxAttempts, cancellationToken);
                        if (delivered) await store.MarkSentAsync(message.MessageId, cancellationToken);
                    }
                    catch (Exception)
                    {
                        // Keep draining the batch even if one message blows up.
                    }
                }
        """;

    private const string OutboxUsing = "using Meridian.Interchange.Outbox;\n";

    internal static readonly WalkthroughScenario Sql = new(
        "sql",
        Meridian,
        [
            new SourceEdit(BookingsController, "using Microsoft.AspNetCore.Mvc;\n", EditKind.InsertAfter, "using Microsoft.Data.SqlClient;\n"),
            new SourceEdit(BookingsController, SampleActionEnd, EditKind.InsertAfter, InlineSqlMethod)
        ],
        [],
        ["data-access/no-inline-sql"],
        []);

    internal static readonly WalkthroughScenario Gateway = new(
        "gateway",
        Meridian,
        [new SourceEdit(BookingsController, SampleActionEnd, EditKind.InsertAfter, GatewayBypassMethod)],
        [],
        ["clearance/engine/containment"],
        []);

    internal static readonly WalkthroughScenario Beat2 = new(
        "beat2",
        Meridian,
        [new SourceEdit(BookingsController, "    [HttpGet(\"sample\")]", EditKind.InsertBefore, LookupMethod)],
        [],
        ["naming/async-suffix"],
        []);

    internal static readonly WalkthroughScenario Beat4 = new(
        "beat4",
        Meridian,
        [new SourceEdit(BookingsController, "GetByReference(", EditKind.Replace, "GetByReferenceAsync(")],
        [],
        [],
        [],
        Beat2);

    internal static readonly WalkthroughScenario Beat5 = new(
        "beat5",
        Meridian,
        [
            new SourceEdit(
                ContainerNumberValidator,
                "        if (containerNumber[3] != 'U') return false;",
                EditKind.Replace,
                "        // ISO 6346 category identifiers: U freight, J detachable equipment, Z trailers and chassis.\n"
                + "        if (containerNumber[3] is not ('U' or 'J' or 'Z')) return false;")
        ],
        [],
        [],
        ["clearance/engine/tripwire"]);

    internal static readonly WalkthroughScenario Transactional = new(
        "transactional",
        Quoting,
        [new SourceEdit(RequestQuoteHandler, "[Transactional]\n", EditKind.Delete)],
        [],
        ["handlers/transactional"],
        []);

    internal static readonly WalkthroughScenario Expire = new(
        "expire",
        Quoting,
        [],
        [new ImaginedFile(ExpireQuotesHandlerPath, ExpireQuotesHandlerSource)],
        ["layering/application-boundaries"],
        []);

    internal static readonly WalkthroughScenario Preview = new(
        "preview",
        Operations,
        [],
        [new ImaginedFile(InvoicePreviewPath, InvoicePreviewSource)],
        ["modules/internals", "modules/dispatch/outbound"],
        []);

    internal static readonly WalkthroughScenario Probe = new(
        "probe",
        Interchange,
        [
            new SourceEdit(
                CarrierClient,
                "        using StringContent content = new(envelope.Payload, Encoding.UTF8, \"application/json\");",
                EditKind.InsertBefore,
                "        using var probe = new HttpClient();\n")
        ],
        [],
        ["http/reuse-httpclient"],
        []);

    internal static readonly WalkthroughScenario Captive = new(
        "captive",
        Interchange,
        [
            new SourceEdit(OutboxDispatcher, "using Meridian.Interchange.Host;\n", EditKind.InsertAfter, OutboxUsing),
            new SourceEdit(
                OutboxDispatcher,
                "OutboxDispatcher(ScopedDispatchRunner runner, IOptions<InterchangeOptions> options, ILogger<OutboxDispatcher> logger)",
                EditKind.Replace,
                "OutboxDispatcher(IOutboxStore store, IOptions<InterchangeOptions> options, ILogger<OutboxDispatcher> logger)"),
            new SourceEdit(
                OutboxDispatcher,
                "                await runner.RunPendingAsync(stoppingToken);",
                EditKind.Replace,
                "                IReadOnlyList<OutboxMessage> pending = await store.GetPendingAsync(stoppingToken);\n"
                + "                logger.LogInformation(\"{Count} outbox messages awaiting transmission.\", pending.Count);")
        ],
        [],
        ["di/hosted-services-scope-their-work", "di/no-captive-dependencies"],
        []);

    internal static readonly WalkthroughScenario Catch = new(
        "catch",
        Interchange,
        [new SourceEdit(OutboxProcessor, DeliveryLoopBody, EditKind.Replace, SwallowingDeliveryLoopBody)],
        [],
        ["exceptions/no-general-catch"],
        []);

    internal static readonly WalkthroughScenario Token = new(
        "token",
        Interchange,
        [
            new SourceEdit(
                ScopedDispatchRunner,
                "public async Task RunPendingAsync(CancellationToken cancellationToken)",
                EditKind.Replace,
                "public async Task RunPendingAsync()"),
            new SourceEdit(
                ScopedDispatchRunner,
                "await processor.ProcessPendingAsync(cancellationToken);",
                EditKind.Replace,
                "await processor.ProcessPendingAsync(CancellationToken.None);"),
            new SourceEdit(OutboxDispatcher, "await runner.RunPendingAsync(stoppingToken);", EditKind.Replace, "await runner.RunPendingAsync();")
        ],
        [],
        ["async/accept-cancellation"],
        []);

    internal static readonly WalkthroughScenario Table = new(
        "table",
        Interchange,
        [
            new SourceEdit(
                OutboxMessage,
                "namespace Meridian.Interchange.Outbox;",
                EditKind.InsertBefore,
                "using System.ComponentModel.DataAnnotations.Schema;\n\n"),
            new SourceEdit(OutboxMessage, "public sealed record OutboxMessage(", EditKind.InsertBefore, "[Table(\"InterchangeOutbox\")]\n")
        ],
        [],
        ["persistence/no-mapping-attributes"],
        []);

    internal static readonly WalkthroughScenario Contract = new(
        "contract",
        Interchange,
        [
            new SourceEdit(PartnerClient, "namespace Meridian.Interchange.Partners;", EditKind.InsertBefore, OutboxUsing + "\n"),
            new SourceEdit(
                PartnerClient,
                "Task SendAsync(PartnerEnvelope envelope, CancellationToken cancellationToken);",
                EditKind.Replace,
                "Task SendAsync(OutboxMessage message, CancellationToken cancellationToken);"),
            .. TypedClientEdits(CarrierClient),
            .. TypedClientEdits(CustomsFilingClient),
            .. TypedClientEdits(TerminalClient),
            new SourceEdit(LegacyManifestClient, "using Meridian.Interchange.Configuration;\n", EditKind.InsertAfter, OutboxUsing),
            new SourceEdit(
                LegacyManifestClient,
                "public Task SendAsync(PartnerEnvelope envelope,",
                EditKind.Replace,
                "public Task SendAsync(OutboxMessage message,"),
            new SourceEdit(
                LegacyManifestClient,
                "SerializeAsync(envelope, cancellationToken)",
                EditKind.Replace,
                "SerializeAsync(message, cancellationToken)"),
            new SourceEdit(
                LegacyManifestClient,
                "private Task<string> SerializeAsync(PartnerEnvelope envelope,",
                EditKind.Replace,
                "private Task<string> SerializeAsync(OutboxMessage message,"),
            new SourceEdit(
                LegacyManifestClient,
                "id=\"{envelope.MessageId}\">{envelope.Payload}</manifest>",
                EditKind.Replace,
                "id=\"{message.MessageId}\">{message.Payload}</manifest>"),
            new SourceEdit(
                OutboxProcessor,
                "await partner.SendAsync(new PartnerEnvelope(message.MessageId, message.Payload), cancellationToken);",
                EditKind.Replace,
                "await partner.SendAsync(message, cancellationToken);")
        ],
        [],
        ["contracts/no-entity-exposure"],
        []);

    internal static readonly WalkthroughScenario Snapshot = new(
        "snapshot",
        Interchange,
        [
            new SourceEdit(
                OutboxDispatcher,
                "IOptions<InterchangeOptions> options",
                EditKind.Replace,
                "IOptionsSnapshot<InterchangeOptions> options")
        ],
        [],
        ["di/hosted-services-scope-their-work"],
        []);

    internal static readonly WalkthroughScenario Rename = new(
        "rename",
        Interchange,
        [
            new SourceEdit(OutboxProcessorPort, "Task ProcessPendingAsync(", EditKind.Replace, "Task ProcessPending("),
            new SourceEdit(OutboxProcessor, "public async Task ProcessPendingAsync(", EditKind.Replace, "public async Task ProcessPending("),
            new SourceEdit(ScopedDispatchRunner, "processor.ProcessPendingAsync(", EditKind.Replace, "processor.ProcessPending(")
        ],
        [],
        ["naming/async-suffix"],
        []);

    /// <summary>Every scenario, grouped by example in the order the pages introduce them.</summary>
    internal static readonly IReadOnlyList<WalkthroughScenario> All =
    [
        Sql, Gateway, Beat2, Beat4, Beat5,
        Transactional, Expire,
        Preview,
        Probe, Captive, Catch, Token, Table, Contract, Snapshot, Rename
    ];

    /// <summary>The four examples, in the order a reader lists the directory in.</summary>
    internal static readonly IReadOnlyList<string> Examples = [Meridian, Quoting, Operations, Interchange];

    /// <summary>The scenario keyed <paramref name="key" />.</summary>
    internal static WalkthroughScenario Named(string key)
    {
        return All.SingleOrDefault(scenario => string.Equals(scenario.Key, key, StringComparison.Ordinal))
               ?? throw new InvalidOperationException($"No walkthrough scenario is keyed '{key}'.");
    }

    // The partner envelope's change in one of the three typed clients: the entity's using, the parameter, and
    // the payload read that follows it.
    private static IEnumerable<SourceEdit> TypedClientEdits(string client)
    {
        yield return new SourceEdit(client, "using System.Text;\n", EditKind.InsertAfter, OutboxUsing);
        yield return new SourceEdit(client, "SendAsync(PartnerEnvelope envelope,", EditKind.Replace, "SendAsync(OutboxMessage message,");
        yield return new SourceEdit(client, "new(envelope.Payload,", EditKind.Replace, "new(message.Payload,");
    }
}
