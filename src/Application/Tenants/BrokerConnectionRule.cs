using FluentValidation.Results;
using MyWealthV2.Application.Broker;
using ValidationException = MyWealthV2.Application.Common.Exceptions.ValidationException;

namespace MyWealthV2.Application.Tenants;

/// <summary>Orders R3: a Tenant names a broker connection that is configured on this host.</summary>
internal static class BrokerConnectionRule
{
    public const string Message = "Unknown broker connection.";

    /// <summary>400 <c>errors.BrokerConnection</c> when <paramref name="name"/> is given and is not configured.</summary>
    public static void Ensure(IBrokerConnections connections, string? name)
    {
        if (name is not null && !connections.IsConfigured(name))
        {
            throw new ValidationException([new ValidationFailure("BrokerConnection", Message)]);
        }
    }
}
