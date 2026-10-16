namespace SupportDesk.Application.Features.Tickets.Triage;

/// <summary>An agent as the assigner sees them: only the facts the rules need.</summary>
public sealed record AgentCandidate(
    int Id,
    string FullName,
    bool IsActive,
    int MaxOpenTickets,
    int OpenTickets,
    IReadOnlyCollection<int> SpecializedCategoryIds);

/// <summary>Who owns the ticket (null when nobody is eligible) and why.</summary>
public sealed record AssignmentDecision(int? AgentId, string Reason);

/// <summary>
/// BR-4 and BR-5: among eligible agents, the one with the fewest open tickets; the lowest id
/// breaks a tie, so the same input always gives the same answer. Having nobody eligible is a
/// normal outcome, not an error.
/// </summary>
public static class AgentAssigner
{
    public static AssignmentDecision Assign(
        int categoryId,
        string categoryName,
        bool requiresSpecialist,
        IReadOnlyCollection<AgentCandidate> candidates)
    {
        var eligible = candidates
            .Where(a => IsEligible(a, categoryId, requiresSpecialist, a.OpenTickets))
            .OrderBy(a => a.OpenTickets)
            .ThenBy(a => a.Id)
            .ToList();

        if (eligible.Count == 0)
        {
            return new AssignmentDecision(
                null,
                requiresSpecialist
                    ? $"Left unassigned: no active {categoryName} specialist is below their open-ticket limit."
                    : "Left unassigned: no active agent is below their open-ticket limit.");
        }

        var chosen = eligible[0];
        return new AssignmentDecision(
            chosen.Id,
            $"Assigned to {chosen.FullName}: fewest open tickets ({chosen.OpenTickets}) among {eligible.Count} eligible agent(s).");
    }

    /// <summary>
    /// BR-7: keep the current agent if they are still eligible, otherwise choose again. The
    /// ticket is already counted in the current agent's open tickets, so it is not counted
    /// against them a second time.
    /// </summary>
    public static AssignmentDecision ReEvaluate(
        int categoryId,
        string categoryName,
        bool requiresSpecialist,
        int? currentAgentId,
        IReadOnlyCollection<AgentCandidate> candidates)
    {
        var current = currentAgentId is null
            ? null
            : candidates.FirstOrDefault(a => a.Id == currentAgentId);

        if (current is not null
            && IsEligible(current, categoryId, requiresSpecialist, current.OpenTickets - 1))
        {
            return new AssignmentDecision(current.Id, $"Kept with {current.FullName}, who is still eligible.");
        }

        return Assign(categoryId, categoryName, requiresSpecialist, candidates);
    }

    private static bool IsEligible(
        AgentCandidate agent, int categoryId, bool requiresSpecialist, int openTickets) =>
        agent.IsActive
        && openTickets < agent.MaxOpenTickets
        && (!requiresSpecialist || agent.SpecializedCategoryIds.Contains(categoryId));
}
