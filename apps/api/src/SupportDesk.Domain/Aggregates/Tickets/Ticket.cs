using SupportDesk.Domain.Common;
using SupportDesk.Domain.Exceptions;

namespace SupportDesk.Domain.Aggregates.Tickets;

/// <summary>
/// A support request raised by a customer. Every change to a ticket goes through one of its
/// methods, so its rules cannot be bypassed.
/// </summary>
/// <remarks>
/// The customer, the category and the assigned agent are other aggregates and are referenced
/// by id only.
/// </remarks>
public sealed class Ticket : AggregateRoot
{
    private Ticket()
    {
        // For EF Core.
    }

    /// <summary>Human-friendly identifier shown to customers, e.g. TCK-0042.</summary>
    public string Reference { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public int CustomerId { get; private set; }

    public int CategoryId { get; private set; }

    /// <summary>Null while the ticket is waiting for an owner.</summary>
    public int? AssignedAgentId { get; private set; }

    public TicketPriority Priority { get; private set; }

    public TicketStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>When the team has committed to responding by. Null when no SLA applies.</summary>
    public DateTime? DueAtUtc { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    /// <summary>
    /// When the current SLA window started: when triage applied, or at the latest escalation.
    /// Null means the window started when the ticket was created.
    /// </summary>
    public DateTime? SlaStartedAtUtc { get; private set; }

    /// <summary>True while the ticket still needs work from an agent.</summary>
    public bool IsOpen => Status is not (TicketStatus.Resolved or TicketStatus.Closed);

    /// <summary>
    /// Raises a new ticket for a customer. It starts as <see cref="TicketStatus.New"/>, with no
    /// owner and no due date.
    /// </summary>
    public static Ticket Raise(
        string reference,
        string title,
        string description,
        int customerId,
        int categoryId,
        TicketPriority priority,
        DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new Ticket
        {
            Reference = reference,
            Title = title.Trim(),
            Description = description.Trim(),
            CustomerId = customerId,
            CategoryId = categoryId,
            Priority = priority,
            Status = TicketStatus.New,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc
        };
    }

    /// <summary>
    /// Moves the ticket through its lifecycle, keeping the resolution timestamp in step.
    /// </summary>
    /// <exception cref="BusinessRuleViolationException">The ticket is closed.</exception>
    public void ChangeStatus(TicketStatus status, DateTime nowUtc)
    {
        if (Status == TicketStatus.Closed && status != TicketStatus.Closed)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {Reference} is closed and cannot be moved to {status}.");
        }

        ResolvedAtUtc = status switch
        {
            TicketStatus.Resolved => nowUtc,
            TicketStatus.Closed => ResolvedAtUtc ?? nowUtc,
            _ => null
        };

        Status = status;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Hands the ticket to an agent, or takes it back off them when <paramref name="agentId"/>
    /// is null. Whether the agent may take it is checked by the caller, which can see agents.
    /// </summary>
    public void AssignTo(int? agentId, DateTime nowUtc)
    {
        AssignedAgentId = agentId;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Records the outcome of triage on a freshly raised ticket: when it is due and, if anyone
    /// was eligible, who owns it. The status stays as it is.
    /// </summary>
    public void ApplyTriage(DateTime dueAtUtc, int? assignedAgentId, DateTime nowUtc)
    {
        DueAtUtc = dueAtUtc;
        SlaStartedAtUtc = nowUtc;
        AssignTo(assignedAgentId, nowUtc);
    }

    /// <summary>
    /// The priority this ticket would have after an escalation: one level up.
    /// </summary>
    /// <exception cref="BusinessRuleViolationException">
    /// The ticket is Resolved or Closed, or is already Critical.
    /// </exception>
    public TicketPriority PriorityAfterEscalation()
    {
        if (!IsOpen)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {Reference} is {Status} and cannot be escalated.");
        }

        if (Priority == TicketPriority.Critical)
        {
            throw new BusinessRuleViolationException(
                $"Ticket {Reference} is already Critical and cannot be escalated further.");
        }

        // Priorities are ordered, lowest urgency first, and are never renumbered.
        return Priority + 1;
    }

    /// <summary>
    /// Raises the priority one level and records who did it and why. The caller has already
    /// worked out the new due date and owner, because it can see customer tiers and agents.
    /// </summary>
    /// <returns>The history row to store with the change.</returns>
    /// <exception cref="BusinessRuleViolationException">The ticket cannot be escalated.</exception>
    public TicketEscalation Escalate(
        string reason, string escalatedBy, DateTime dueAtUtc, int? assignedAgentId, DateTime nowUtc)
    {
        var newPriority = PriorityAfterEscalation();

        // Built first: a bad reason or actor throws before anything on the ticket changes.
        var record = TicketEscalation.Record(
            Id, Priority, newPriority, AssignedAgentId, assignedAgentId,
            DueAtUtc, dueAtUtc, reason, escalatedBy, nowUtc);

        Priority = newPriority;
        AssignedAgentId = assignedAgentId;
        DueAtUtc = dueAtUtc;
        SlaStartedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;

        return record;
    }
}
