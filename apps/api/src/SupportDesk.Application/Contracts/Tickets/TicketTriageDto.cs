namespace SupportDesk.Application.Contracts.Tickets;

/// <summary>Why triage decided what it did. Returned when a ticket is raised.</summary>
public sealed record TicketTriageDto(string PriorityReason, string SlaReason, string AssignmentReason);
