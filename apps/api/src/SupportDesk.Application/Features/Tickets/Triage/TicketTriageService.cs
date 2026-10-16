using System.Globalization;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Features.Tickets.Triage;

/// <summary>Everything triage needs to know about a new ticket, already loaded.</summary>
public sealed record TriageInput(
    TicketPriority? RequestedPriority,
    int CategoryId,
    string CategoryName,
    bool CategoryRequiresSpecialist,
    bool CategoryForcesCritical,
    CustomerTier CustomerTier,
    DateTime NowUtc,
    IReadOnlyCollection<AgentCandidate> Candidates);

/// <summary>What triage decided, with a human-readable reason for each decision.</summary>
public sealed record TriageResult(
    TicketPriority Priority,
    DateTime DueAtUtc,
    int? AssignedAgentId,
    string PriorityReason,
    string SlaReason,
    string AssignmentReason);

/// <summary>
/// Combines the priority, SLA and assignment rules into one decision. It reads nothing and
/// writes nothing, so the handler stays responsible for loading and saving.
/// </summary>
public sealed class TicketTriageService(SlaCalculator sla)
{
    public TriageResult TriageNewTicket(TriageInput input)
    {
        var priority = PriorityResolver.Resolve(
            input.RequestedPriority, input.CategoryForcesCritical, input.CategoryName);

        var window = sla.WindowFor(priority.Priority, input.CustomerTier);

        var assignment = AgentAssigner.Assign(
            input.CategoryId, input.CategoryName, input.CategoryRequiresSpecialist, input.Candidates);

        return new TriageResult(
            priority.Priority,
            input.NowUtc + window,
            assignment.AgentId,
            priority.Reason,
            DescribeWindow(priority.Priority, input.CustomerTier, window),
            assignment.Reason);
    }

    private string DescribeWindow(TicketPriority priority, CustomerTier tier, TimeSpan window)
    {
        var standard = sla.WindowFor(priority, CustomerTier.Standard);

        return tier == CustomerTier.Premium && window != standard
            ? $"{priority} priority allows {Hours(standard)}; Premium customers get {Hours(window)}."
            : $"{priority} priority allows {Hours(window)}.";
    }

    private static string Hours(TimeSpan span) =>
        $"{span.TotalHours.ToString("0.##", CultureInfo.InvariantCulture)} h";
}