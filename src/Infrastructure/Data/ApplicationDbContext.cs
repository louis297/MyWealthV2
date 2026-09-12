using System.Reflection;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data.Entities;
using MyWealthV2.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MyWealthV2.Infrastructure.Data;

public class ApplicationDbContext : IdentityUserContext<ApplicationUser>, IApplicationDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> DomainUsers => Set<User>();

    DbSet<User> IApplicationDbContext.Users => DomainUsers;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<UserToken> UserTokenSeams => Set<UserToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
