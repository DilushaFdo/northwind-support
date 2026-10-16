using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Application.Features.Tickets.Triage;

/// <summary>
/// The "Sla" configuration section: how long each priority is given to be resolved.
/// </summary>
public sealed class SlaOptions
{
    public const string SectionName = "Sla";

    public Dictionary<TicketPriority, double> BaseWindowHours { get; set; } = new();

    public double PremiumCustomerMultiplier { get; set; } = 0.5;

    public double MinimumWindowHours { get; set; } = 1;

    public double AtRiskThresholdPercent { get; set; } = 25;
}