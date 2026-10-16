using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Triage;

public class TicketTriageServiceTests
{
    private static readonly DateTime Now = FixedClock.DefaultNow;

    private static TicketTriageService Service() => new(new SlaCalculator(TestSlaOptions.Create()));

    private static AgentCandidate Agent(int id, int open, int[]? specs = null) =>
        new(id, $"Agent {id}", true, 10, open, specs ?? []);

    private static TriageInput Input(
        TicketPriority? requested = null,
        CustomerTier tier = CustomerTier.Standard,
        bool forcesCritical = false,
        bool requiresSpecialist = false,
        int categoryId = 1,
        IReadOnlyCollection<AgentCandidate>? candidates = null) =>
        new(requested, categoryId, "Category", requiresSpecialist, forcesCritical, tier, Now, candidates ?? []);

    [Fact]
    public void StandardCustomer_GetsTheRequestedPriorityAndTheBaseWindow()
    {
        var result = Service().TriageNewTicket(Input(requested: TicketPriority.High));

        Assert.Equal(TicketPriority.High, result.Priority);
        Assert.Equal(Now.AddHours(8), result.DueAtUtc);
    }

    [Fact]
    public void PremiumCustomer_GetsHalfTheWindow()
    {
        var result = Service().TriageNewTicket(Input(tier: CustomerTier.Premium));

        Assert.Equal(TicketPriority.Medium, result.Priority);
        Assert.Equal(Now.AddHours(12), result.DueAtUtc);
    }

    [Fact]
    public void ForcingCategory_OverridesTheRequestedPriority_AndSetsTheWindowFromIt()
    {
        var result = Service().TriageNewTicket(
            Input(requested: TicketPriority.Low, tier: CustomerTier.Premium, forcesCritical: true));

        Assert.Equal(TicketPriority.Critical, result.Priority);
        Assert.Equal(Now.AddHours(2), result.DueAtUtc);
    }

    [Fact]
    public void AssignsTheLeastLoadedEligibleAgent()
    {
        var result = Service().TriageNewTicket(Input(candidates: [Agent(1, 5), Agent(2, 3)]));

        Assert.Equal(2, result.AssignedAgentId);
    }

    [Fact]
    public void SpecialistCategory_AssignsOnlyASpecialist()
    {
        var result = Service().TriageNewTicket(Input(
            requiresSpecialist: true,
            categoryId: 3,
            candidates: [Agent(1, 0), Agent(2, 4, specs: [3])]));

        Assert.Equal(2, result.AssignedAgentId);
    }

    [Fact]
    public void NobodyEligible_StillGivesPriorityAndDueDate_AndLeavesTheTicketUnassigned()
    {
        var result = Service().TriageNewTicket(Input(requested: TicketPriority.High, requiresSpecialist: true));

        Assert.Null(result.AssignedAgentId);
        Assert.Equal(Now.AddHours(8), result.DueAtUtc);
        Assert.Contains("unassigned", result.AssignmentReason);
    }

    [Fact]
    public void EveryDecisionComesWithAReason()
    {
        var result = Service().TriageNewTicket(Input(candidates: [Agent(1, 0)]));

        Assert.False(string.IsNullOrWhiteSpace(result.PriorityReason));
        Assert.False(string.IsNullOrWhiteSpace(result.SlaReason));
        Assert.False(string.IsNullOrWhiteSpace(result.AssignmentReason));
    }
}
