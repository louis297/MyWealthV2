namespace MyWealthV2.Domain.Instruments;

/// <summary>One row of a tax attributes write: a tax year, its FIF treatment and an optional note.</summary>
public sealed record FifTreatmentInput(int TaxYear, FifTreatment Treatment, string? Note);

/// <summary>Why a tax attributes write cannot be stored: the field it belongs to and the message.</summary>
public sealed record TaxAttributeRefusal(string Field, string Message);
