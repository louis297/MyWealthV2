namespace MyWealthV2.Web.Infrastructure;

/// <summary>
/// Refuses a query string key the route does not take with 400 and the key in <c>errors</c> (for example
/// bank-reconciliation R1: no caller sends fields the route does not define). Keys compare without regard to case.
/// </summary>
public sealed class KnownQueryKeysFilter(params string[] keys) : IEndpointFilter
{
    private readonly HashSet<string> _keys = new(keys, StringComparer.OrdinalIgnoreCase);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var unknown = context.HttpContext.Request.Query.Keys.Where(key => !_keys.Contains(key)).ToList();
        if (unknown.Count > 0)
        {
            return TypedResults.ValidationProblem(unknown.ToDictionary(
                key => key,
                key => new[] { $"{key} is not a field of this route." }));
        }

        return await next(context);
    }
}
