using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.UnitTests.TestDoubles;

/// <summary>The SLA numbers from appsettings, for tests that need a configured calculator.</summary>
public static class TestSlaOptions
{
    public static SlaOptions Create() => new()
    {
        BaseWindowHours = new()
        {
            [TicketPriority.Low] = 72,
            [TicketPriority.Medium] = 24,
            [TicketPriority.High] = 8,
            [TicketPriority.Critical] = 4,
        },
        PremiumCustomerMultiplier = 0.5,
        MinimumWindowHours = 1,
        AtRiskThresholdPercent = 25,
    };
}
