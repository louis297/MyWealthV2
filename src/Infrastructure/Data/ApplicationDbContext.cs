using System.Reflection;
using FluentValidation.Results;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Infrastructure.Data.Entities;
using MyWealthV2.Infrastructure.Data.Interceptors;
using MyWealthV2.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MyWealthV2.Infrastructure.Data;

public class ApplicationDbContext : IdentityUserContext<ApplicationUser>, IApplicationDbContext
{
    private readonly IServiceProvider? _services;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, IServiceProvider services)
        : base(options)
    {
        _services = services;
    }

    public DbSet<User> DomainUsers => Set<User>();

    DbSet<User> IApplicationDbContext.Users => DomainUsers;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<UserToken> UserTokenSeams => Set<UserToken>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await base.SaveChangesAsync(cancellationToken);
            await DispatchDomainEvents(cancellationToken);
            return result;
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            throw new ValidationException([
                new ValidationFailure(string.Empty, "A unique constraint was violated.")
            ]);
        }
    }

    private async Task DispatchDomainEvents(CancellationToken cancellationToken)
    {
        var mediator = _services?.GetService<IMediator>();
        if (mediator is null)
        {
            return;
        }

        await DispatchDomainEventsInterceptor.PublishAfterCommitAsync(this, mediator, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException sql && sql.Number is 2601 or 2627;
}
