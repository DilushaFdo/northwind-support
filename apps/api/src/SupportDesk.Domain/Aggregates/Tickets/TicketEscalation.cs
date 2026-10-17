namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// One escalation of a ticket: what it was before, what it became, and who asked and why.
/// Rows are only ever added, never changed, and only <see cref="Ticket.Escalate"/> can
/// create one.
/// </summary>
public sealed class TicketEscalation
{
    public const int MinReasonLength = 5;

    public const int MaxReasonLength = 500;

    public const int MaxEscalatedByLength = 100;

    private TicketEscalation()
    {
        // For EF Core.
    }

    public int Id { get; private set; }

    public int TicketId { get; private set; }

    public TicketPriority FromPriority { get; private set; }

    public TicketPriority ToPriority { get; private set; }

    public int? FromAgentId { get; private set; }

    public int? ToAgentId { get; private set; }

    /// <summary>Null when the ticket had no due date before this escalation.</summary>
    public DateTime? FromDueAtUtc { get; private set; }

    public DateTime ToDueAtUtc { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public string EscalatedBy { get; private set; } = string.Empty;

    public DateTime EscalatedAtUtc { get; private set; }

    internal static TicketEscalation Record(
        int ticketId,
        TicketPriority fromPriority,
        TicketPriority toPriority,
        int? fromAgentId,
        int? toAgentId,
        DateTime? fromDueAtUtc,
        DateTime toDueAtUtc,
        string reason,
        string escalatedBy,
        DateTime escalatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(escalatedBy);

        var trimmedReason = reason.Trim();
        var trimmedActor = escalatedBy.Trim();

        if (trimmedReason.Length is < MinReasonLength or > MaxReasonLength)
        {
            throw new ArgumentException(
                $"The reason must be {MinReasonLength} to {MaxReasonLength} characters.", nameof(reason));
        }

        if (trimmedActor.Length > MaxEscalatedByLength)
        {
            throw new ArgumentException(
                $"The escalating user must be at most {MaxEscalatedByLength} characters.", nameof(escalatedBy));
        }

        return new TicketEscalation
        {
            TicketId = ticketId,
            FromPriority = fromPriority,
            ToPriority = toPriority,
            FromAgentId = fromAgentId,
            ToAgentId = toAgentId,
            FromDueAtUtc = fromDueAtUtc,
            ToDueAtUtc = toDueAtUtc,
            Reason = trimmedReason,
            EscalatedBy = trimmedActor,
            EscalatedAtUtc = escalatedAtUtc
        };
    }
}
