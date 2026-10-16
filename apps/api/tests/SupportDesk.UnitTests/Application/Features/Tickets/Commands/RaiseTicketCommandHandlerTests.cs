using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Commands;

public class RaiseTicketCommandHandlerTests
{
    private static CreateTicketRequest Request(TicketPriority? priority = null) =>
        new("Cannot log in to the portal", "Happens for every user since this morning.", 1, 3, priority);

    [Fact]
    public async Task HandleAsync_StampsReferenceStatusAndTimestamps()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        var created = context.AddedTicket;
        Assert.NotNull(created);
        Assert.Equal("TCK-0041", created.Reference);
        Assert.Equal(TicketStatus.New, created.Status);
        Assert.Equal(TicketPriority.High, created.Priority);
        Assert.Equal(FixedClock.DefaultNow, created.CreatedAtUtc);
        Assert.Equal(FixedClock.DefaultNow, created.UpdatedAtUtc);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_WithoutRequestedPriority_FallsBackToMedium()
    {
        var context = new TicketCommandTestContext();

        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Equal(TicketPriority.Medium, context.AddedTicket.Priority);
    }

    [Fact]
    public async Task HandleAsync_WhenCustomerDoesNotExist_Throws()
    {
        var context = new TicketCommandTestContext();
        context.TriageQueries
            .Setup(q => q.GetCustomerTierAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CustomerTier?)null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        Assert.Contains("Customer", exception.Message);
        context.VerifySaved(Times.Never());
    }

    [Fact]
    public async Task HandleAsync_WhenCategoryDoesNotExist_Throws()
    {
        var context = new TicketCommandTestContext();
        context.TriageQueries
            .Setup(q => q.GetCategoryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CategoryTriageInfo?)null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => context.RaiseTicket.HandleAsync(Request(), CancellationToken.None));

        Assert.Contains("Category", exception.Message);
        context.VerifySaved(Times.Never());
    }

    private static void WithAgents(TicketCommandTestContext context, params AgentCandidate[] agents) =>
        context.TriageQueries
            .Setup(q => q.GetAgentCandidatesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(agents);

    private static void WithCategory(
        TicketCommandTestContext context, string name, bool requiresSpecialist = false, bool forcesCritical = false) =>
        context.TriageQueries
            .Setup(q => q.GetCategoryAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CategoryTriageInfo(3, name, requiresSpecialist, forcesCritical));

    [Fact]
    public async Task HandleAsync_AssignsTheLeastLoadedAgent_AndSetsTheDueDate()
    {
        var context = new TicketCommandTestContext();
        WithAgents(
            context,
            new AgentCandidate(1, "Agent 1", true, 10, 5, []),
            new AgentCandidate(2, "Agent 2", true, 10, 3, []));

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        var created = context.AddedTicket;
        Assert.NotNull(created);
        Assert.Equal(2, created.AssignedAgentId);
        Assert.Equal(FixedClock.DefaultNow.AddHours(8), created.DueAtUtc);
        Assert.Equal(TicketStatus.New, created.Status);
    }

    [Fact]
    public async Task HandleAsync_ForPremiumCustomer_HalvesTheWindow()
    {
        var context = new TicketCommandTestContext();
        context.TriageQueries
            .Setup(q => q.GetCustomerTierAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CustomerTier.Premium);

        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.Equal(FixedClock.DefaultNow.AddHours(12), context.AddedTicket?.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_ForCriticalForcingCategory_OverridesTheRequestedPriority()
    {
        var context = new TicketCommandTestContext();
        WithCategory(context, "Security", forcesCritical: true);

        await context.RaiseTicket.HandleAsync(Request(TicketPriority.Low), CancellationToken.None);

        Assert.Equal(TicketPriority.Critical, context.AddedTicket?.Priority);
        Assert.Equal(FixedClock.DefaultNow.AddHours(4), context.AddedTicket?.DueAtUtc);
    }

    [Fact]
    public async Task HandleAsync_ForSpecialistCategory_AssignsOnlyASpecialist()
    {
        var context = new TicketCommandTestContext();
        WithCategory(context, "Billing", requiresSpecialist: true);
        WithAgents(
            context,
            new AgentCandidate(1, "Agent 1", true, 10, 0, []),
            new AgentCandidate(2, "Agent 2", true, 10, 4, [3]));

        await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.Equal(2, context.AddedTicket?.AssignedAgentId);
    }

    [Fact]
    public async Task HandleAsync_WhenNobodyIsEligible_StillRaisesTheTicket_Unassigned()
    {
        var context = new TicketCommandTestContext();
        WithCategory(context, "Billing", requiresSpecialist: true);

        var result = await context.RaiseTicket.HandleAsync(Request(TicketPriority.High), CancellationToken.None);

        Assert.NotNull(context.AddedTicket);
        Assert.Null(context.AddedTicket.AssignedAgentId);
        Assert.Equal(FixedClock.DefaultNow.AddHours(8), context.AddedTicket.DueAtUtc);
        Assert.NotNull(result.Triage);
        Assert.Contains("unassigned", result.Triage.AssignmentReason);
        context.VerifySaved(Times.Once());
    }

    [Fact]
    public async Task HandleAsync_ReturnsTheReasonForEachDecision()
    {
        var context = new TicketCommandTestContext();

        var result = await context.RaiseTicket.HandleAsync(Request(), CancellationToken.None);

        Assert.NotNull(result.Triage);
        Assert.False(string.IsNullOrWhiteSpace(result.Triage.PriorityReason));
        Assert.False(string.IsNullOrWhiteSpace(result.Triage.SlaReason));
        Assert.False(string.IsNullOrWhiteSpace(result.Triage.AssignmentReason));
    }
}
