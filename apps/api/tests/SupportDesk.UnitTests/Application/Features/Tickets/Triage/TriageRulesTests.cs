using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.UnitTests.TestDoubles;

namespace SupportDesk.UnitTests.Application.Features.Tickets.Triage;

public class PriorityResolverTests
{
    [Fact]
    public void RequestedPriority_IsUsed()
    {
        var decision = PriorityResolver.Resolve(TicketPriority.High, false, "General");

        Assert.Equal(TicketPriority.High, decision.Priority);
    }

    [Fact]
    public void NoRequestedPriority_DefaultsToMedium()
    {
        var decision = PriorityResolver.Resolve(null, false, "General");

        Assert.Equal(TicketPriority.Medium, decision.Priority);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(TicketPriority.Low)]
    [InlineData(TicketPriority.High)]
    public void ForcingCategory_AlwaysGivesCritical(TicketPriority? requested)
    {
        var decision = PriorityResolver.Resolve(requested, true, "Security");

        Assert.Equal(TicketPriority.Critical, decision.Priority);
        Assert.Contains("Security", decision.Reason);
    }
}

public class SlaCalculatorTests
{
    private static SlaCalculator Calculator(Action<SlaOptions>? configure = null)
    {
        var options = new SlaOptions
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

        configure?.Invoke(options);
        return new SlaCalculator(options);
    }

    [Theory]
    [InlineData(TicketPriority.Low, 72.0)]
    [InlineData(TicketPriority.Medium, 24.0)]
    [InlineData(TicketPriority.High, 8.0)]
    [InlineData(TicketPriority.Critical, 4.0)]
    public void StandardCustomer_GetsTheBaseWindow(TicketPriority priority, double hours)
    {
        var window = Calculator().WindowFor(priority, CustomerTier.Standard);

        Assert.Equal(TimeSpan.FromHours(hours), window);
    }

    [Theory]
    [InlineData(TicketPriority.Low, 36.0)]
    [InlineData(TicketPriority.Medium, 12.0)]
    [InlineData(TicketPriority.High, 4.0)]
    [InlineData(TicketPriority.Critical, 2.0)]
    public void PremiumCustomer_GetsHalfTheWindow(TicketPriority priority, double hours)
    {
        var window = Calculator().WindowFor(priority, CustomerTier.Premium);

        Assert.Equal(TimeSpan.FromHours(hours), window);
    }

    [Fact]
    public void PremiumWindow_NeverDropsBelowTheFloor()
    {
        var calculator = Calculator(o => o.BaseWindowHours[TicketPriority.Critical] = 1.5);

        var window = calculator.WindowFor(TicketPriority.Critical, CustomerTier.Premium);

        Assert.Equal(TimeSpan.FromHours(1), window);
    }

    [Fact]
    public void PremiumWindow_ExactlyAtTheFloor_IsKept()
    {
        var calculator = Calculator(o => o.BaseWindowHours[TicketPriority.Critical] = 2);

        var window = calculator.WindowFor(TicketPriority.Critical, CustomerTier.Premium);

        Assert.Equal(TimeSpan.FromHours(1), window);
    }

    [Fact]
    public void Window_FollowsConfiguration()
    {
        var calculator = Calculator(o => o.PremiumCustomerMultiplier = 0.25);

        var window = calculator.WindowFor(TicketPriority.Low, CustomerTier.Premium);

        Assert.Equal(TimeSpan.FromHours(18), window);
    }

    [Fact]
    public void DueAt_IsTheStartPlusTheWindow()
    {
        var due = Calculator().DueAt(FixedClock.DefaultNow, TicketPriority.High, CustomerTier.Standard);

        Assert.Equal(FixedClock.DefaultNow.AddHours(8), due);
    }

    [Fact]
    public void MissingPriorityInConfiguration_Throws()
    {
        var calculator = Calculator(o => o.BaseWindowHours.Remove(TicketPriority.High));

        Assert.Throws<InvalidOperationException>(
            () => calculator.WindowFor(TicketPriority.High, CustomerTier.Standard));
    }
}