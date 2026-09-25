namespace MyWealthV2.Domain.Entities;

public sealed class IdempotencyRecord
{
    private IdempotencyRecord()
    {
    }

    public int Id { get; private set; }

    public int TenantId { get; private set; }

    public Guid Key { get; private set; }

    public string Method { get; private set; } = string.Empty;

    public string Path { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public int ResponseStatus { get; private set; }

    public string ResponseBody { get; private set; } = string.Empty;

    public DateTimeOffset Created { get; private set; }

    public static IdempotencyRecord Create(
        int tenantId,
        Guid key,
        string method,
        string path,
        string requestHash,
        int responseStatus,
        string responseBody,
        DateTimeOffset created) =>
        new()
        {
            TenantId = tenantId,
            Key = key,
            Method = method,
            Path = path,
            RequestHash = requestHash,
            ResponseStatus = responseStatus,
            ResponseBody = responseBody,
            Created = created
        };
}
