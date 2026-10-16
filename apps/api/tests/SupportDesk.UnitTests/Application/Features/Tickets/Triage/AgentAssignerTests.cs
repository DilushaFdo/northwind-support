using SupportDesk.Application.Features.Tickets.Triage;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Triage;

public class AgentAssignerTests
{
    private const int General = 1;
    private const int Billing = 3;

    private static AgentCandidate Agent(
        int id, int open, int max = 10, bool active = true, int[]? specs = null) =>
        new(id, $"Agent {id}", active, max, open, specs ?? []);

    private static int? Assign(bool requiresSpecialist, int categoryId, params AgentCandidate[] agents) =>
        AgentAssigner.Assign(categoryId, "Billing", requiresSpecialist, agents).AgentId;

    [Fact]
    public void FewestOpenTickets_Wins()
    {
        var chosen = Assign(false, General, Agent(1, 5), Agent(2, 3), Agent(3, 4));

        Assert.Equal(2, chosen);
    }

    [Fact]
    public void Tie_GoesToTheLowestId_WhateverTheInputOrder()
    {
        var forwards = Assign(false, General, Agent(1, 2), Agent(2, 2), Agent(3, 2));
        var backwards = Assign(false, General, Agent(3, 2), Agent(2, 2), Agent(1, 2));

        Assert.Equal(1, forwards);
        Assert.Equal(1, backwards);
    }

    [Fact]
    public void AgentOneBelowTheLimit_CanTakeTheTicket()
    {
        var chosen = Assign(false, General, Agent(1, open: 5, max: 6));

        Assert.Equal(1, chosen);
    }

    [Fact]
    public void AgentAtTheLimit_IsNotEligible()
    {
        var chosen = Assign(false, General, Agent(1, open: 6, max: 6), Agent(2, open: 5, max: 6));

        Assert.Equal(2, chosen);
    }

    [Fact]
    public void InactiveAgent_IsNeverChosen()
    {
        var chosen = Assign(false, General, Agent(1, 0, active: false), Agent(2, 7));

        Assert.Equal(2, chosen);
    }

    [Fact]
    public void SpecialistCategory_OnlySpecialistsAreEligible()
    {
        var chosen = Assign(
            true, Billing,
            Agent(1, 0),
            Agent(2, 4, specs: [Billing]),
            Agent(3, 1, specs: [General]));

        Assert.Equal(2, chosen);
    }

    [Fact]
    public void NonSpecialistCategory_IgnoresSpecializations()
    {
        var chosen = Assign(false, General, Agent(1, 0), Agent(2, 4, specs: [General]));

        Assert.Equal(1, chosen);
    }

    [Fact]
    public void NobodyEligible_LeavesTheTicketUnassigned_WithAReason()
    {
        var decision = AgentAssigner.Assign(
            Billing, "Billing", true, [Agent(1, 0), Agent(2, 6, max: 6, specs: [Billing])]);

        Assert.Null(decision.AgentId);
        Assert.Contains("Billing", decision.Reason);
    }

    [Fact]
    public void NoCandidatesAtAll_LeavesTheTicketUnassigned()
    {
        var decision = AgentAssigner.Assign(General, "General", false, []);

        Assert.Null(decision.AgentId);
        Assert.False(string.IsNullOrWhiteSpace(decision.Reason));
    }

    [Fact]
    public void ReEvaluate_KeepsTheCurrentAgent_EvenWhenSomeoneElseHasFewerTickets()
    {
        var decision = AgentAssigner.ReEvaluate(
            General, "General", false, currentAgentId: 1, [Agent(1, 5), Agent(2, 0)]);

        Assert.Equal(1, decision.AgentId);
    }

    [Fact]
    public void ReEvaluate_KeepsAnAgentAtTheirLimit_BecauseTheTicketIsAlreadyTheirs()
    {
        var decision = AgentAssigner.ReEvaluate(
            General, "General", false, currentAgentId: 1, [Agent(1, open: 6, max: 6), Agent(2, 0)]);

        Assert.Equal(1, decision.AgentId);
    }

    [Fact]
    public void ReEvaluate_Reassigns_WhenTheCurrentAgentIsInactive()
    {
        var decision = AgentAssigner.ReEvaluate(
            General, "General", false, currentAgentId: 1, [Agent(1, 0, active: false), Agent(2, 3)]);

        Assert.Equal(2, decision.AgentId);
    }

    [Fact]
    public void ReEvaluate_Reassigns_WhenTheCurrentAgentLacksTheSpecialization()
    {
        var decision = AgentAssigner.ReEvaluate(
            Billing, "Billing", true, currentAgentId: 1, [Agent(1, 0), Agent(2, 3, specs: [Billing])]);

        Assert.Equal(2, decision.AgentId);
    }

    [Fact]
    public void ReEvaluate_WithNoCurrentAgent_ChoosesLikeANewTicket()
    {
        var decision = AgentAssigner.ReEvaluate(
            General, "General", false, currentAgentId: null, [Agent(1, 5), Agent(2, 3)]);

        Assert.Equal(2, decision.AgentId);
    }
}
