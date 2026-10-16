using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Features.Tickets.Triage;

/// <summary>
/// BR-2 and BR-3: how long a ticket has to be resolved, and when that runs out. All numbers
/// come from <see cref="SlaOptions"/>, so they change without touching code.
/// </summary>
public sealed class SlaCalculator(SlaOptions options)
{
    public TimeSpan WindowFor(TicketPriority priority, CustomerTier tier)
    {
        if (!options.BaseWindowHours.TryGetValue(priority, out var baseHours))
        {
            throw new InvalidOperationException(
                $"No SLA base window is configured for priority {priority}.");
        }

        var hours = tier == CustomerTier.Premium
            ? Math.Max(baseHours * options.PremiumCustomerMultiplier, options.MinimumWindowHours)
            : baseHours;

        return TimeSpan.FromHours(hours);
    }

    public DateTime DueAt(DateTime startUtc, TicketPriority priority, CustomerTier tier) =>
        startUtc + WindowFor(priority, tier);
}
