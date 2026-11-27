using MyWealthV2.Application.Common.Security;

namespace MyWealthV2.Application.TaxYears.Queries.GetCustomerTaxYear;

/// <summary>Tax-year-events R1, R4, R30–R32: one Customer's tax events and holdings of one tax year, in NZD.</summary>
[Authorize(Policy = Policies.AccountsRead)]
public record GetCustomerTaxYearQuery(Guid CustomerId, string? TaxYear) : IRequest<CustomerTaxYearDto>;

public class GetCustomerTaxYearQueryValidator : AbstractValidator<GetCustomerTaxYearQuery>
{
    public GetCustomerTaxYearQueryValidator(TimeProvider time)
    {
    }
}

public class GetCustomerTaxYearQueryHandler : IRequestHandler<GetCustomerTaxYearQuery, CustomerTaxYearDto>
{
    public Task<CustomerTaxYearDto> Handle(GetCustomerTaxYearQuery request, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
