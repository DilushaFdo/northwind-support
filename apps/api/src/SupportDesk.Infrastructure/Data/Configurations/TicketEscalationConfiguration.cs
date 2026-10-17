using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Aggregates.Tickets;

namespace SupportDesk.Infrastructure.Data.Configurations;

/// <remarks>
/// An audit trail: nothing is deleted with its ticket or agent, so every foreign key restricts.
/// </remarks>
public sealed class TicketEscalationConfiguration : IEntityTypeConfiguration<TicketEscalation>
{
    public void Configure(EntityTypeBuilder<TicketEscalation> builder)
    {
        builder.ToTable("TicketEscalations", t =>
        {
            t.HasCheckConstraint("CK_TicketEscalations_FromPriority", "[FromPriority] BETWEEN 1 AND 4");
            t.HasCheckConstraint("CK_TicketEscalations_ToPriority", "[ToPriority] BETWEEN 1 AND 4");
            t.HasCheckConstraint("CK_TicketEscalations_PriorityRaised", "[ToPriority] > [FromPriority]");
        });

        builder.HasKey(e => e.Id);

        builder.Property(e => e.FromPriority).HasConversion<int>();

        builder.Property(e => e.ToPriority).HasConversion<int>();

        builder.Property(e => e.Reason).HasMaxLength(TicketEscalation.MaxReasonLength).IsRequired();

        builder.Property(e => e.EscalatedBy).HasMaxLength(TicketEscalation.MaxEscalatedByLength).IsRequired();

        builder.Property(e => e.FromDueAtUtc).HasColumnType("datetime2(3)");

        builder.Property(e => e.ToDueAtUtc).HasColumnType("datetime2(3)");

        builder.Property(e => e.EscalatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasOne<Ticket>()
            .WithMany()
            .HasForeignKey(e => e.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(e => e.FromAgentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Agent>()
            .WithMany()
            .HasForeignKey(e => e.ToAgentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Serves "the history of one ticket, newest first". Its leading column also covers the
        // TicketId foreign key, so there is no separate index for it.
        builder.HasIndex(e => new { e.TicketId, e.EscalatedAtUtc })
            .IsDescending(false, true)
            .HasDatabaseName("IX_TicketEscalations_TicketId_EscalatedAtUtc");
    }
}
