using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation.Results;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Reports;

public static class CustomerTransactionsBinder
{
    public const string Code = "customer-transactions";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static void Validate(JsonElement parameters)
    {
        CustomerTransactionsInput input;
        try
        {
            input = parameters.Deserialize<CustomerTransactionsInput>(Options)
                    ?? throw Unknown();
        }
        catch (JsonException exception) when (exception.Message.Contains("could not be mapped", StringComparison.OrdinalIgnoreCase))
        {
            throw Unknown();
        }
        catch (JsonException)
        {
            throw new ValidationException([
                new ValidationFailure("parameters", "parameters are not valid.")
            ]);
        }

        if (input.CustomerId is null || input.CustomerId == Guid.Empty)
        {
            throw new ValidationException([
                new ValidationFailure("customerId", "customerId is required.")
            ]);
        }

        if (input.From is null)
        {
            throw new ValidationException([
                new ValidationFailure("from", "from is required.")
            ]);
        }

        if (input.To is null)
        {
            throw new ValidationException([
                new ValidationFailure("to", "to is required.")
            ]);
        }

        if (input.From > input.To)
        {
            throw new ValidationException([
                new ValidationFailure("from", "from must be on or before to.")
            ]);
        }
    }

    private static ValidationException Unknown() =>
        new([new ValidationFailure("parameters", "unknown field.")]);

    private sealed class CustomerTransactionsInput
    {
        public Guid? CustomerId { get; set; }

        public DateOnly? From { get; set; }

        public DateOnly? To { get; set; }

        public bool? IncludeClosedAccounts { get; set; }
    }
}
