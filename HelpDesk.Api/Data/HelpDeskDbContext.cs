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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var ticket = modelBuilder.Entity<Ticket>();

        ticket.HasKey(ticket => ticket.Id);
        ticket.Property(ticket => ticket.Id).UseIdentityByDefaultColumn();
        ticket.Property(ticket => ticket.Title).IsRequired().HasMaxLength(100);
        ticket.Property(ticket => ticket.Description).IsRequired();
        ticket.Property(ticket => ticket.CreatedAt).HasColumnType("timestamp with time zone");
    }
}
