using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Queries;

public sealed class TriageQueriesTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Candidates_CountOnlyOpenTicketsPerAgent_AndCarryTheirSpecializations()
    {
        int generalId, billingId;

        using (var db = _database.CreateContext())
        {
            var general = new Category("General", false, false);
            var billing = new Category("Billing", true, false);
            var customer = new Customer("Contoso", "it@contoso.test", null, CustomerTier.Premium, FixedClock.DefaultNow);
            var alex = new Agent("Alex Turner", "alex@test.example", 10, FixedClock.DefaultNow);
            var marcus = new Agent("Marcus Doyle", "marcus@test.example", 6, FixedClock.DefaultNow);
            var yuki = new Agent("Yuki Tanaka", "yuki@test.example", 8, FixedClock.DefaultNow);
            yuki.Deactivate();

            db.AddRange(general, billing, customer, alex, marcus, yuki);
            db.SaveChanges();

            alex.AddSpecialization(general.Id);
            marcus.AddSpecialization(billing.Id);
            marcus.AddSpecialization(general.Id);
            db.SaveChanges();

            generalId = general.Id;
            billingId = billing.Id;

            Ticket Build(string reference, int? agentId, TicketStatus status) =>
                new TicketBuilder()
                    .WithReference(reference)
                    .ForCustomer(customer.Id)
                    .InCategory(general.Id)
                    .AssignedTo(agentId)
                    .WithStatus(status)
                    .Build();

            db.AddRange(
                Build("TCK-0001", alex.Id, TicketStatus.Open),
                Build("TCK-0002", alex.Id, TicketStatus.InProgress),
                Build("TCK-0003", alex.Id, TicketStatus.Resolved),
                Build("TCK-0004", alex.Id, TicketStatus.Closed),
                Build("TCK-0005", marcus.Id, TicketStatus.New),
                Build("TCK-0006", null, TicketStatus.New));
            db.SaveChanges();
        }

        using var queryDb = _database.CreateContext();
        var candidates = await new TriageQueries(queryDb).GetAgentCandidatesAsync(CancellationToken.None);

        Assert.Equal(3, candidates.Count);

        var alexCandidate = candidates[0];
        Assert.Equal(2, alexCandidate.OpenTickets);
        Assert.Equal([generalId], alexCandidate.SpecializedCategoryIds);

        var marcusCandidate = candidates[1];
        Assert.Equal(1, marcusCandidate.OpenTickets);
        Assert.Equal(new[] { generalId, billingId }.Order(), marcusCandidate.SpecializedCategoryIds.Order());

        var yukiCandidate = candidates[2];
        Assert.False(yukiCandidate.IsActive);
        Assert.Equal(0, yukiCandidate.OpenTickets);
    }
}
