using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Exceptions;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Domain;

public class TicketEscalationTests
{
    private static readonly DateTime Now = FixedClock.DefaultNow.AddHours(3);
    private static readonly DateTime NewDue = Now.AddHours(4);

    private static TicketBuilder Builder(TicketPriority priority = TicketPriority.Medium) =>
        new TicketBuilder().WithPriority(priority);

    private static TicketEscalation Escalate(Ticket ticket, string reason = "Customer is blocked", string by = "Alex Turner") =>
        ticket.Escalate(reason, by, NewDue, 2, Now);

    [Theory]
    [InlineData(TicketPriority.Low, TicketPriority.Medium)]
    [InlineData(TicketPriority.Medium, TicketPriority.High)]
    [InlineData(TicketPriority.High, TicketPriority.Critical)]
    public void Escalate_RaisesThePriorityByExactlyOneLevel(TicketPriority before, TicketPriority after)
    {
        var ticket = Builder(before).Build();

        Escalate(ticket);

        Assert.Equal(after, ticket.Priority);
    }

    [Fact]
    public void Escalate_AppliesTheNewDueDateOwnerAndWindowStart()
    {
        var ticket = Builder().AssignedTo(1).Build();

        Escalate(ticket);

        Assert.Equal(NewDue, ticket.DueAtUtc);
        Assert.Equal(2, ticket.AssignedAgentId);
        Assert.Equal(Now, ticket.SlaStartedAtUtc);
        Assert.Equal(Now, ticket.UpdatedAtUtc);
    }

    [Fact]
    public void Escalate_ReturnsTheHistoryRowWithBeforeAndAfter()
    {
        var ticket = Builder(TicketPriority.High).Build();
        var originalDue = FixedClock.DefaultNow.AddHours(8);
        ticket.ApplyTriage(originalDue, 1, FixedClock.DefaultNow);

        var row = ticket.Escalate("  Customer is blocked  ", "  Alex Turner ", NewDue, 3, Now);

        Assert.Equal(TicketPriority.High, row.FromPriority);
        Assert.Equal(TicketPriority.Critical, row.ToPriority);
        Assert.Equal(1, row.FromAgentId);
        Assert.Equal(3, row.ToAgentId);
        Assert.Equal(originalDue, row.FromDueAtUtc);
        Assert.Equal(NewDue, row.ToDueAtUtc);
        Assert.Equal("Customer is blocked", row.Reason);
        Assert.Equal("Alex Turner", row.EscalatedBy);
        Assert.Equal(Now, row.EscalatedAtUtc);
    }

    [Fact]
    public void Escalate_FromATicketWithNoDueDate_RecordsANullPreviousDueDate()
    {
        var row = Escalate(Builder().Build());

        Assert.Null(row.FromDueAtUtc);
        Assert.Null(row.FromAgentId);
    }

    [Fact]
    public void Escalate_OnACriticalTicket_IsRejected_AndChangesNothing()
    {
        var ticket = Builder(TicketPriority.Critical).AssignedTo(1).Build();

        var exception = Assert.Throws<BusinessRuleViolationException>(() => Escalate(ticket));

        Assert.Contains("Critical", exception.Message);
        Assert.Equal(TicketPriority.Critical, ticket.Priority);
        Assert.Equal(1, ticket.AssignedAgentId);
        Assert.Null(ticket.SlaStartedAtUtc);
    }

    [Theory]
    [InlineData(TicketStatus.Resolved)]
    [InlineData(TicketStatus.Closed)]
    public void Escalate_OnAResolvedOrClosedTicket_IsRejected_NamingTheStatus(TicketStatus status)
    {
        var ticket = Builder().WithStatus(status).Build();

        var exception = Assert.Throws<BusinessRuleViolationException>(() => Escalate(ticket));

        Assert.Contains(status.ToString(), exception.Message);
        Assert.Equal(TicketPriority.Medium, ticket.Priority);
    }

    [Fact]
    public void Escalate_OnAResolvedCriticalTicket_ReportsTheResolvedState()
    {
        var ticket = Builder(TicketPriority.Critical).WithStatus(TicketStatus.Resolved).Build();

        var exception = Assert.Throws<BusinessRuleViolationException>(() => Escalate(ticket));

        Assert.Contains("Resolved", exception.Message);
        Assert.DoesNotContain("Critical", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abcd")]
    [InlineData("  abcd  ")]
    public void Escalate_WithATooShortReason_IsRejected_AndChangesNothing(string reason)
    {
        var ticket = Builder().Build();

        Assert.ThrowsAny<ArgumentException>(() => Escalate(ticket, reason));

        Assert.Equal(TicketPriority.Medium, ticket.Priority);
        Assert.Null(ticket.DueAtUtc);
    }

    [Fact]
    public void Escalate_AcceptsReasonsAtBothLengthLimits()
    {
        Escalate(Builder().Build(), new string('x', TicketEscalation.MinReasonLength));
        Escalate(Builder().Build(), new string('x', TicketEscalation.MaxReasonLength));
    }

    [Fact]
    public void Escalate_WithATooLongReason_IsRejected()
    {
        Assert.ThrowsAny<ArgumentException>(
            () => Escalate(Builder().Build(), new string('x', TicketEscalation.MaxReasonLength + 1)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Escalate_WithoutAnActor_IsRejected(string actor)
    {
        Assert.ThrowsAny<ArgumentException>(() => Escalate(Builder().Build(), by: actor));
    }

    [Fact]
    public void Escalate_AcceptsAnActorAtTheLengthLimit_AndRejectsOneOverIt()
    {
        Escalate(Builder().Build(), by: new string('a', TicketEscalation.MaxEscalatedByLength));

        Assert.ThrowsAny<ArgumentException>(
            () => Escalate(Builder().Build(), by: new string('a', TicketEscalation.MaxEscalatedByLength + 1)));
    }

    [Fact]
    public void PriorityAfterEscalation_ReportsTheNextLevel_WithoutChangingTheTicket()
    {
        var ticket = Builder(TicketPriority.Low).Build();

        Assert.Equal(TicketPriority.Medium, ticket.PriorityAfterEscalation());
        Assert.Equal(TicketPriority.Low, ticket.Priority);
    }
}
