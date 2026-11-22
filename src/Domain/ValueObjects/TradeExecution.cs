namespace MyWealthV2.Domain.ValueObjects;

/// <summary>
/// What the broker said about one fill, as <see cref="Entities.Transaction.PostBuy"/> and
/// <see cref="Entities.Transaction.PostSell"/> take it (orders R32, R35): the fill's PublicId, the trade date, the
/// settlement date and the broker's contract note. The price is the trade's own price.
/// </summary>
public sealed record TradeExecution(Guid FillPublicId, DateOnly TradeDate, DateOnly SettlementDate, string? BrokerReference);
