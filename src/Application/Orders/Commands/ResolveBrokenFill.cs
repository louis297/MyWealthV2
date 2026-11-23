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

/// <summary>Trade-settlement R31: a TenantAdmin who corrected the account with an adjustment marks a break that
/// could not be applied as resolved.</summary>
[Authorize(Policy = Policies.TransactionsAdjust)]
public sealed class ResolveBrokenFillCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    [JsonIgnore]
    public Guid FillId { get; set; }

    public string? Note { get; init; }

    public string? RowVersion { get; init; }

    public JsonElement? TenantId { get; init; }
}

public class ResolveBrokenFillCommandValidator : AbstractValidator<ResolveBrokenFillCommand>
{
    public ResolveBrokenFillCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note)
            .Must(note => note is not null && note.Trim().Length is >= 1 and <= Order.NoteMaxLength)
            .WithMessage("Note must be 1 to 200 characters.");
        RuleFor(command => command.TenantId).Must(OrderBodyRules.IsAbsent).WithMessage("TenantId cannot be set.");
    }
}

public class ResolveBrokenFillCommandHandler(IApplicationDbContext db, ICurrentUser currentUser, TimeProvider time)
    : IRequestHandler<ResolveBrokenFillCommand>
{
    public async Task Handle(ResolveBrokenFillCommand request, CancellationToken cancellationToken)
    {
        // R1: the order in the caller's scope, of the caller's tenant; else 404, as for the fill.
        var order = await OrderReader.Scope(db, currentUser).Include(row => row.Fills)
                        .SingleOrDefaultAsync(row => row.PublicId == request.Id, cancellationToken)
                    ?? throw new NotFoundException("Order", request.Id);
        var fill = order.Fills.SingleOrDefault(row => row.PublicId == request.FillId)
                   ?? throw new NotFoundException("OrderFill", request.FillId);
        if (!string.Equals(Convert.ToBase64String(order.RowVersion ?? []), request.RowVersion, StringComparison.Ordinal))
        {
            throw new ConcurrencyException();
        }

        var actor = currentUser.Person?.Id ?? throw new ForbiddenAccessException();
        try
        {
            fill.ResolveBreak(actor, request.Note!, time.GetUtcNow());
        }
        catch (OrderStatusException exception)
        {
            throw new ValidationException([new ValidationFailure("Status", exception.Message)]);
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }
}
