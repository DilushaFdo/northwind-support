using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Customers;

namespace SupportDesk.Application.Abstractions;

/// <summary>The category facts triage needs.</summary>
public sealed record CategoryTriageInfo(
    int Id, string Name, bool RequiresSpecialist, bool ForcesCriticalPriority);

/// <summary>
/// Read-only lookups for triage. Separate from the repositories, which hand out aggregates
/// to be modified; these only answer questions.
/// </summary>
public interface ITriageQueries
{
    /// <summary>The customer's tier, or null when the customer does not exist.</summary>
    Task<CustomerTier?> GetCustomerTierAsync(int customerId, CancellationToken ct);

    /// <summary>The category's triage facts, or null when it does not exist.</summary>
    Task<CategoryTriageInfo?> GetCategoryAsync(int categoryId, CancellationToken ct);

    /// <summary>Every agent with their specializations and open-ticket count, in one query.</summary>
    Task<IReadOnlyList<AgentCandidate>> GetAgentCandidatesAsync(CancellationToken ct);
}
