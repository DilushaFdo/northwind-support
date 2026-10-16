using Microsoft.Extensions.Logging;
using SupportDesk.Application.Abstractions;
using SupportDesk.Application.Contracts.Tickets;
using SupportDesk.Application.Exceptions;
using SupportDesk.Application.Features.Tickets.Queries.GetTicket;
using SupportDesk.Application.Features.Tickets.Triage;
using SupportDesk.Domain.Aggregates.Tickets;
using SupportDesk.Domain.Repositories;

namespace SupportDesk.Application.Features.Tickets.Commands.RaiseTicket;

/// <summary>
/// Raises a ticket for a customer and triages it: priority, due date and owner are decided
/// by the rules, not by hand.
/// </summary>
public sealed class RaiseTicketCommandHandler(
    ITicketRepository tickets,
    ITriageQueries triageQueries,
    TicketTriageService triageService,
    IUnitOfWork unitOfWork,
    IClock clock,
    GetTicketQueryHandler getTicket,
    ILogger<RaiseTicketCommandHandler> logger)
{
    /// <exception cref="NotFoundException">The customer or the category does not exist.</exception>
    public async Task<TicketDetailDto> HandleAsync(CreateTicketRequest request, CancellationToken ct)
    {
        var tier = await triageQueries.GetCustomerTierAsync(request.CustomerId, ct)
            ?? throw new NotFoundException("Customer", request.CustomerId);

        var category = await triageQueries.GetCategoryAsync(request.CategoryId, ct)
            ?? throw new NotFoundException("Category", request.CategoryId);

        var now = clock.UtcNow;

        var triage = triageService.TriageNewTicket(new TriageInput(
            request.RequestedPriority,
            category.Id,
            category.Name,
            category.RequiresSpecialist,
            category.ForcesCriticalPriority,
            tier,
            now,
            await triageQueries.GetAgentCandidatesAsync(ct)));

        var ticket = Ticket.Raise(
            await tickets.NextReferenceAsync(ct),
            request.Title,
            request.Description,
            request.CustomerId,
            request.CategoryId,
            triage.Priority,
            now);

        ticket.ApplyTriage(triage.DueAtUtc, triage.AssignedAgentId, now);

        await tickets.AddAsync(ticket, ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Ticket {Reference} raised for customer {CustomerId} in category {CategoryId}: priority {Priority}, agent {AgentId}.",
            ticket.Reference, ticket.CustomerId, ticket.CategoryId, ticket.Priority, ticket.AssignedAgentId);

        var created = await getTicket.HandleAsync(ticket.Id, ct);
        created.Triage = new TicketTriageDto(triage.PriorityReason, triage.SlaReason, triage.AssignmentReason);

        return created;
    }
}
