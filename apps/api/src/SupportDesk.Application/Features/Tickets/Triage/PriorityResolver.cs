using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Features.Tickets.Triage;

/// <summary>The priority a new ticket gets, and why.</summary>
public sealed record PriorityDecision(TicketPriority Priority, string Reason);

/// <summary>
/// BR-1: the requested priority, Medium when none was requested, and always Critical when
/// the category says so. The category's flag decides, never its name.
/// </summary>
public static class PriorityResolver
{
    public const TicketPriority DefaultPriority = TicketPriority.Medium;

    public static PriorityDecision Resolve(
        TicketPriority? requested, bool categoryForcesCritical, string categoryName)
    {
        if (categoryForcesCritical)
        {
            return new PriorityDecision(
                TicketPriority.Critical,
                $"{categoryName} tickets are always Critical, whatever was requested.");
        }

        if (requested is { } priority)
        {
            return new PriorityDecision(priority, $"The requested priority ({priority}) was used.");
        }

        return new PriorityDecision(
            DefaultPriority, $"No priority was requested, so the default ({DefaultPriority}) was used.");
    }
}
