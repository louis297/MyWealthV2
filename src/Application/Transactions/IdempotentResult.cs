namespace MyWealthV2.Application.Transactions;

public sealed record IdempotentResult(int StatusCode, string Body);
