using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Api.Data;

public class HelpDeskDbContext : DbContext
{
    public HelpDeskDbContext(DbContextOptions<HelpDeskDbContext> options)
        : base(options)
    {
    }

    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<User> Users => Set<User>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var comment = modelBuilder.Entity<TicketComment>();
        comment.ToTable("TicketComments", table =>
        {
            table.HasCheckConstraint("CK_TicketComments_Content_NotBlank",
                "length(btrim(\"Content\", U&'\\0009\\000A\\000B\\000C\\000D\\0020\\0085\\00A0\\1680\\2000\\2001\\2002\\2003\\2004\\2005\\2006\\2007\\2008\\2009\\200A\\2028\\2029\\202F\\205F\\3000')) > 0");
            // PostgreSQL counts code points; add the supplementary characters to count UTF-16 units.
            table.HasCheckConstraint("CK_TicketComments_Content_Utf16Length",
                "char_length(\"Content\") + char_length(regexp_replace(\"Content\", U&'[^\\+010000-\\+10FFFF]', '', 'g')) <= 4000");
        });
        comment.HasKey(c => c.Id);
        comment.Property(c => c.Id).UseIdentityByDefaultColumn();
        comment.Property(c => c.Content).IsRequired().HasMaxLength(4000);
        comment.Property(c => c.AuthorNameSnapshot).IsRequired().HasMaxLength(100);
        comment.Property(c => c.CreatedAt).HasColumnType("timestamp with time zone");
        comment.HasOne<Ticket>().WithMany().HasForeignKey(c => c.TicketId).OnDelete(DeleteBehavior.Restrict);
        comment.HasOne<User>().WithMany().HasForeignKey(c => c.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        comment.HasIndex(c => new { c.TicketId, c.Id });

        var user = modelBuilder.Entity<User>();
        user.ToTable("Users", table => table.HasCheckConstraint(
            "CK_Users_Email_Normalized", "\"Email\" = lower(btrim(\"Email\"))"));
        user.HasKey(user => user.Id);
        user.Property(user => user.Id).UseIdentityByDefaultColumn();
        user.Property(user => user.Name).IsRequired().HasMaxLength(100);
        user.Property(user => user.Email).IsRequired().HasMaxLength(254);
        user.HasIndex(user => user.Email).IsUnique();
        user.Property(user => user.PasswordHash).HasColumnType("text").IsRequired(false);
        user.Property(user => user.Role)
            .HasConversion<string>()
            .HasDefaultValue(UserRole.User)
            .HasSentinel(UserRole.User);
        user.Property(user => user.CreatedAt).HasColumnType("timestamp with time zone");

        var ticket = modelBuilder.Entity<Ticket>();

        ticket.HasOne(ticket => ticket.CreatedByUser)
            .WithMany()
            .HasForeignKey(ticket => ticket.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        ticket.HasOne(ticket => ticket.AssignedToUser)
            .WithMany()
            .HasForeignKey(ticket => ticket.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);
        ticket.HasIndex(ticket => ticket.CreatedByUserId);
        ticket.HasIndex(ticket => ticket.AssignedToUserId);

        ticket.HasKey(ticket => ticket.Id);
        ticket.Property(ticket => ticket.Id).UseIdentityByDefaultColumn();
        ticket.Property(ticket => ticket.Title).IsRequired().HasMaxLength(100);
        ticket.Property(ticket => ticket.Description).IsRequired();
        ticket.Property(ticket => ticket.CreatedAt).HasColumnType("timestamp with time zone");
        ticket.Property(ticket => ticket.Status)
            .HasConversion<string>()
            .HasDefaultValue(TicketStatus.Open);
        ticket.Property(ticket => ticket.Priority)
            .HasConversion<string>()
            .HasDefaultValue(TicketPriority.Medium)
            .HasSentinel(TicketPriority.Medium);
        ticket.Property(ticket => ticket.Category)
            .HasConversion<string>()
            .HasDefaultValue(TicketCategory.Other)
            .HasSentinel(TicketCategory.Other);
    }
}
