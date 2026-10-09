namespace Meridian.Interchange.Configuration;

/// <summary>The options bound from the <c>Interchange</c> configuration section.</summary>
public sealed class InterchangeOptions
{
    /// <summary>How long the dispatcher waits between outbox polls.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many attempts a message gets against its partner before it waits for the next poll.</summary>
    public int MaxDeliveryAttempts { get; set; } = 3;

    /// <summary>The endpoint each partner channel transmits to.</summary>
    public PartnerEndpoints Partners { get; set; } = new();
}
