using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain;

public class TicketTriageTests
{
    [Fact]
    public void ApplyTriage_SetsTheDueDateAndTheOwner_AndLeavesTheStatusAlone()
    {
        var ticket = new TicketBuilder().Build();
        var due = FixedClock.DefaultNow.AddHours(8);
        var later = FixedClock.DefaultNow.AddMinutes(1);

        ticket.ApplyTriage(due, 2, later);

        Assert.Equal(due, ticket.DueAtUtc);
        Assert.Equal(2, ticket.AssignedAgentId);
        Assert.Equal(later, ticket.UpdatedAtUtc);
        Assert.Equal(TicketStatus.New, ticket.Status);
    }

    [Fact]
    public void ApplyTriage_WithNoAgent_KeepsTheTicketUnassigned()
    {
        var ticket = new TicketBuilder().Build();

        ticket.ApplyTriage(FixedClock.DefaultNow.AddHours(24), null, FixedClock.DefaultNow);

        Assert.Null(ticket.AssignedAgentId);
        Assert.NotNull(ticket.DueAtUtc);
    }
}
