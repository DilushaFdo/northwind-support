using Microsoft.EntityFrameworkCore;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Categories;
using SupportDesk.Domain.Aggregates.Customers;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Queries;

/// <summary>Read-only lookups for triage. Never tracked, and projected in the database.</summary>
public sealed class TriageQueries(SupportDbContext db) : ITriageQueries
{
    public Task<CustomerTier?> GetCustomerTierAsync(int customerId, CancellationToken ct) =>
        db.Set<Customer>().AsNoTracking()
            .Where(c => c.Id == customerId)
            .Select(c => (CustomerTier?)c.Tier)
            .FirstOrDefaultAsync(ct);

    public Task<CategoryTriageInfo?> GetCategoryAsync(int categoryId, CancellationToken ct) =>
        db.Set<Category>().AsNoTracking()
            .Where(c => c.Id == categoryId)
            .Select(c => new CategoryTriageInfo(c.Id, c.Name, c.RequiresSpecialist, c.ForcesCriticalPriority))
            .FirstOrDefaultAsync(ct);

    /// <remarks>
    /// One statement for all agents: each agent's open count is a subquery, so there is no
    /// query per agent. "Open" is the same condition as <c>Ticket.IsOpen</c>, which EF cannot
    /// translate.
    /// </remarks>
    public async Task<IReadOnlyList<AgentCandidate>> GetAgentCandidatesAsync(CancellationToken ct)
    {
        var rows = await db.Set<Agent>().AsNoTracking()
            .OrderBy(a => a.Id)
            .Select(a => new
            {
                a.Id,
                a.FullName,
                a.IsActive,
                a.MaxOpenTickets,
                OpenTickets = db.Set<Ticket>().Count(t =>
                    t.AssignedAgentId == a.Id
                    && t.Status != TicketStatus.Resolved
                    && t.Status != TicketStatus.Closed),
                CategoryIds = a.Specializations.Select(s => s.CategoryId).ToList()
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new AgentCandidate(
                r.Id, r.FullName, r.IsActive, r.MaxOpenTickets, r.OpenTickets, r.CategoryIds))
            .ToList();
    }
}
