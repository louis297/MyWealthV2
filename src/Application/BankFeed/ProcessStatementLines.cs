using Microsoft.Extensions.Logging;
using MyWealthV2.Application.Common.Interfaces;

namespace MyWealthV2.Application.BankFeed;

/// <summary>
/// Processes every <c>Received</c> credit line of one connection, oldest first: posts the deposit of a receipt that
/// quotes one Custodial account's deposit reference, or leaves it unmatched with a reason (deposit-matching R13,
/// R15–R20, R32; ADR 0021 rule 8). Each line is one database transaction.
/// </summary>
public sealed class ProcessStatementLines
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrencyCatalog _catalog;
    private readonly TimeProvider _time;
    private readonly ILogger<ProcessStatementLines> _logger;

    public ProcessStatementLines(
        IApplicationDbContext db,
        ICurrencyCatalog catalog,
        TimeProvider time,
        ILogger<ProcessStatementLines> logger)
    {
        _db = db;
        _catalog = catalog;
        _time = time;
        _logger = logger;
    }

    public Task RunAsync(string connection, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
