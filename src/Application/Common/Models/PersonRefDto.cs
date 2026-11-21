namespace MyWealthV2.Application.Common.Models;

/// <summary>A person on an item: their PublicId and name.</summary>
public sealed record PersonRefDto(Guid Id, string Name);
