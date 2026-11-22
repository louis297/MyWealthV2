using System.Text.Json;
using System.Text.Json.Serialization;
using MyWealthV2.Application.Common.Exceptions;
using MyWealthV2.Application.Common.Interfaces;
using MyWealthV2.Application.Common.Security;
using MyWealthV2.Domain.Entities;
using MyWealthV2.Domain.MarketData;
using NotFoundException = MyWealthV2.Application.Common.Exceptions.NotFoundException;

namespace MyWealthV2.Application.CorporateActions;

/// <summary>Corporate-actions R15–R17: a TenantAdmin enters a corporate action of an instrument no connection serves.</summary>
[Authorize(Policy = Policies.CorporateActionsManage)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CreateCorporateActionCommand : IRequest<Guid>
{
    public Guid? InstrumentId { get; init; }

    public string? Kind { get; init; }

    public int? RatioNew { get; init; }

    public int? RatioHeld { get; init; }

    public DateOnly? ExDate { get; init; }

    public DateOnly? RecordDate { get; init; }

    public DateOnly? PaymentDate { get; init; }

    public string? Memo { get; init; }

    public JsonElement? TenantId { get; init; }
}

/// <summary>Corporate-actions R18: a TenantAdmin changes a <c>Manual</c> action that is still <c>Announced</c>.</summary>
[Authorize(Policy = Policies.CorporateActionsManage)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateCorporateActionCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? Kind { get; init; }

    public int? RatioNew { get; init; }

    public int? RatioHeld { get; init; }

    public DateOnly? ExDate { get; init; }

    public DateOnly? RecordDate { get; init; }

    public DateOnly? PaymentDate { get; init; }

    public string? Memo { get; init; }

    public string? RowVersion { get; init; }

    public JsonElement? TenantId { get; init; }
}

/// <summary>Corporate-actions R19: a TenantAdmin cancels a <c>Manual</c> action that is still <c>Announced</c>.</summary>
[Authorize(Policy = Policies.CorporateActionsManage)]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CancelCorporateActionCommand : IRequest
{
    [JsonIgnore]
    public Guid Id { get; set; }

    public string? RowVersion { get; init; }

    public string? Note { get; init; }

    public JsonElement? TenantId { get; init; }
}

public class CreateCorporateActionCommandValidator : AbstractValidator<CreateCorporateActionCommand>
{
    public CreateCorporateActionCommandValidator()
    {
        RuleFor(command => command.InstrumentId).NotNull();
        RuleFor(command => command.Kind).Must(CorporateActionTerms.IsKind).WithMessage(CorporateActionTerms.KindMessage);
        RuleFor(command => command.RatioNew).NotNull();
        RuleFor(command => command.RatioHeld).NotNull().WithName("RatioNew").OverridePropertyName("RatioNew");
        RuleFor(command => command.ExDate).NotNull();
        RuleFor(command => command.Memo).Must(CorporateActionTerms.IsMemo).WithMessage(CorporateActionTerms.MemoMessage);
        RuleFor(command => command.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be set; the tenant is the caller's.");
    }
}

public class UpdateCorporateActionCommandValidator : AbstractValidator<UpdateCorporateActionCommand>
{
    public UpdateCorporateActionCommandValidator()
    {
        RuleFor(command => command.Kind).Must(CorporateActionTerms.IsKind).WithMessage(CorporateActionTerms.KindMessage);
        RuleFor(command => command.RatioNew).NotNull();
        RuleFor(command => command.RatioHeld).NotNull().WithName("RatioNew").OverridePropertyName("RatioNew");
        RuleFor(command => command.ExDate).NotNull();
        RuleFor(command => command.Memo).Must(CorporateActionTerms.IsMemo).WithMessage(CorporateActionTerms.MemoMessage);
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be set; the tenant is the caller's.");
    }
}

public class CancelCorporateActionCommandValidator : AbstractValidator<CancelCorporateActionCommand>
{
    public CancelCorporateActionCommandValidator()
    {
        RuleFor(command => command.RowVersion).NotEmpty();
        RuleFor(command => command.Note)
            .Must(note => note?.Trim().Length is >= 1 and <= CorporateAction.NoteMaxLength)
            .WithMessage("Note must be 1 to 200 characters.");
        RuleFor(command => command.TenantId)
            .Must(tenantId => tenantId is null)
            .WithMessage("TenantId cannot be set; the tenant is the caller's.");
    }
}

public class CreateCorporateActionCommandHandler
    : IRequestHandler<CreateCorporateActionCommand, Guid>
{
    public async Task<Guid> Handle(CreateCorporateActionCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}

public class UpdateCorporateActionCommandHandler
    : IRequestHandler<UpdateCorporateActionCommand>
{
    public async Task Handle(UpdateCorporateActionCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}

public class CancelCorporateActionCommandHandler
    : IRequestHandler<CancelCorporateActionCommand>
{
    public async Task Handle(CancelCorporateActionCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
