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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var user = modelBuilder.Entity<User>();
        user.ToTable("Users", table => table.HasCheckConstraint(
            "CK_Users_Email_Normalized", "\"Email\" = lower(btrim(\"Email\"))"));
        user.HasKey(user => user.Id);
        user.Property(user => user.Id).UseIdentityByDefaultColumn();
        user.Property(user => user.Name).IsRequired().HasMaxLength(100);
        user.Property(user => user.Email).IsRequired().HasMaxLength(254);
        user.HasIndex(user => user.Email).IsUnique();
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
