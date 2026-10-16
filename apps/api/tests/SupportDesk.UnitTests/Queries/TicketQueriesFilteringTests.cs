using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Queries;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Queries;

public sealed class TicketQueriesFilteringTests : IDisposable
{
    private readonly SqliteTestDatabase _database = new();
    private readonly int _contosoId;
    private readonly int _fabrikamId;
    private readonly int _categoryId;

    public TicketQueriesFilteringTests()
    {
        using var db = _database.CreateContext();

        var contoso = new Customer("Contoso", "it@contoso.test", null, CustomerTier.Premium, FixedClock.DefaultNow);
        var fabrikam = new Customer("Fabrikam", "it@fabrikam.test", null, CustomerTier.Standard, FixedClock.DefaultNow);
        var category = new Category("General", requiresSpecialist: false, forcesCriticalPriority: false);

        db.AddRange(contoso, fabrikam, category);
        db.SaveChanges();

        _contosoId = contoso.Id;
        _fabrikamId = fabrikam.Id;
        _categoryId = category.Id;
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Priority_IsAnExactMatch()
    {
        Seed(
            Ticket("TCK-0001").WithPriority(TicketPriority.High),
            Ticket("TCK-0002").WithPriority(TicketPriority.Low),
            Ticket("TCK-0003").WithPriority(TicketPriority.High),
            Ticket("TCK-0004").WithPriority(TicketPriority.Medium));

        var result = await Query(new TicketQuery { Priority = TicketPriority.High });

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, item => Assert.Equal(TicketPriority.High, item.Priority));
        Assert.Equal(["TCK-0001", "TCK-0003"], result.Items.Select(i => i.Reference).Order());
    }

    [Fact]
    public async Task SeveralFilters_AreCombinedWithAnd()
    {
        Seed(
            Ticket("TCK-0001").ForCustomer(_contosoId).WithPriority(TicketPriority.High).WithStatus(TicketStatus.Open),
            Ticket("TCK-0002").ForCustomer(_contosoId).WithPriority(TicketPriority.High).WithStatus(TicketStatus.New),
            Ticket("TCK-0003").ForCustomer(_contosoId).WithPriority(TicketPriority.Low).WithStatus(TicketStatus.Open),
            Ticket("TCK-0004").ForCustomer(_fabrikamId).WithPriority(TicketPriority.High).WithStatus(TicketStatus.Open));

        var result = await Query(new TicketQuery
        {
            CustomerId = _contosoId,
            Priority = TicketPriority.High,
            Status = TicketStatus.Open,
        });

        var only = Assert.Single(result.Items);
        Assert.Equal("TCK-0001", only.Reference);
        Assert.Equal(1, result.TotalCount);
    }

    [Theory]
    [InlineData("Contoso")]
    [InlineData("  Contoso  ")]
    [InlineData("onto")]
    public async Task Search_MatchesTheCustomerName_TrimmingSurroundingSpaces(string search)
    {
        Seed(
            Ticket("TCK-0001").ForCustomer(_contosoId).WithTitle("Printer jam"),
            Ticket("TCK-0002").ForCustomer(_contosoId).WithTitle("Cannot log in"),
            Ticket("TCK-0003").ForCustomer(_fabrikamId).WithTitle("Invoice question"));

        var result = await Query(new TicketQuery { Search = search });

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(["TCK-0001", "TCK-0002"], result.Items.Select(i => i.Reference).Order());
    }

    [Fact]
    public async Task BlankSearch_IsIgnored()
    {
        Seed(
            Ticket("TCK-0001").ForCustomer(_contosoId),
            Ticket("TCK-0002").ForCustomer(_fabrikamId));

        var result = await Query(new TicketQuery { Search = "   " });

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task TotalCount_DescribesTheFilteredSet_NotTheWholeTableOrThePage()
    {
        Seed(
            Ticket("TCK-0001").WithPriority(TicketPriority.High),
            Ticket("TCK-0002").WithPriority(TicketPriority.High),
            Ticket("TCK-0003").WithPriority(TicketPriority.High),
            Ticket("TCK-0004").WithPriority(TicketPriority.High),
            Ticket("TCK-0005").WithPriority(TicketPriority.High),
            Ticket("TCK-0006").WithPriority(TicketPriority.Low),
            Ticket("TCK-0007").WithPriority(TicketPriority.Low));

        var result = await Query(new TicketQuery { Priority = TicketPriority.High, PageSize = 2 });

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
    }

    private TicketBuilder Ticket(string reference) =>
        new TicketBuilder().WithReference(reference).ForCustomer(_contosoId).InCategory(_categoryId);

    private void Seed(params TicketBuilder[] builders)
    {
        using var db = _database.CreateContext();
        db.AddRange(builders.Select(b => b.Build()));
        db.SaveChanges();
    }

    private async Task<SupportDesk.Application.Contracts.Common.PagedResult<TicketListItemDto>> Query(TicketQuery query)
    {
        using var db = _database.CreateContext();
        return await new TicketQueries(db, new FixedClock()).GetPagedAsync(query.Normalized(), CancellationToken.None);
    }
}
