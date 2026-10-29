using ITTicketIntelligence.Domain.Tickets;
using ITTicketIntelligence.Infrastructure.Persistence.Attachments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ITTicketIntelligence.Infrastructure.Persistence.Configurations;

internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<AttachmentRecord>
{
    public void Configure(EntityTypeBuilder<AttachmentRecord> b)
    {
        b.ToTable("ticket_attachments", t =>
        {
            t.HasCheckConstraint("ck_attachments_length", "length BETWEEN 1 AND 5242880 AND octet_length(data) = length");
            t.HasCheckConstraint("ck_attachments_type", "content_type IN ('image/png','image/jpeg','application/pdf','text/plain')");
            t.HasCheckConstraint("ck_attachments_hash", "sha256 ~ '^[0-9a-f]{64}$'");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FileName).HasMaxLength(180).IsRequired(); b.Property(x => x.ContentType).HasMaxLength(32).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(64).IsRequired(); b.Property(x => x.Data).IsRequired();
        b.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.TicketId, x.CreatedAt });
    }
}
