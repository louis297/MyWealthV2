using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation.Results;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.Exceptions;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Orders.Commands;

/// <summary>
/// Orders R18: cancel an order. An order the broker cannot hold yet is <c>Cancelled</c> in the request; any other open
/// order gets a cancel request that the worker takes to the broker.
/// </summary>
[Authorize(Policy = Policies.OrdersPlace)]
public sealed class CancelOrderCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    /// <summary>Refused when present (R1).</summary>
    public JsonElement? TenantId { get; init; }
}

public class CancelOrderCommandValidator : AbstractValidator<CancelOrderCommand>
{
    public CancelOrderCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note)
            .Must(note => note is null || note.Trim().Length is >= 1 and <= Order.NoteMaxLength)
            .WithMessage("Note must be 1 to 200 characters.");
        RuleFor(command => command.TenantId).Must(OrderBodyRules.IsAbsent).WithMessage("TenantId cannot be set.");
    }
}

public class CancelOrderCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<CancelOrderCommand>
{
    public async Task Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        // R1: an order outside scope, of another tenant, or missing is 404.
        var order = await OrderReader.Scope(db, currentUser)
                        .SingleOrDefaultAsync(row => row.PublicId == request.Id, cancellationToken)
                    ?? throw new NotFoundException("Order", request.Id);
        if (!string.Equals(Convert.ToBase64String(order.RowVersion ?? []), request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        var actor = currentUser.Person?.Id ?? throw new ForbiddenAccessException();
        var now = time.GetUtcNow();
        try
        {
            // The broker cannot hold an order that was never sent, so it ends here and reserves nothing; any other
            // open order is the broker's to end.
            if (order.IsUnsent)
            {
                order.CancelUnsent(actor, request.Note, now);
            }
            else
            {
                order.RequestCancel(actor, request.Note, now);
            }
        }
        catch (OrderStatusException exception)
        {
            throw new ValidationException([new ValidationFailure("Status", exception.Message)]);
        }

        try
        {
            // The order's row version guards the race with the send step: an attempt recorded since the read is a 409.
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }
}
