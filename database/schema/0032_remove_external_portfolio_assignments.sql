-- Custody modes (docs/features/custody-modes.md §6, R16). Data only, forward only.
-- A portfolio is assigned to a Custodial account only. Every Active assignment (Status = 0) on an External
-- account (CustodyMode = 0) becomes Removed (Status = 1), as an unassign does: no trade is written and the
-- holdings stay. A second run finds no row.

UPDATE pa
SET [Status] = 1,
    [LastModified] = SYSDATETIMEOFFSET(),
    [LastModifiedBy] = N'system'
FROM [AccountPortfolioAssignments] pa
JOIN [Accounts] a ON a.[Id] = pa.[AccountId]
WHERE pa.[Status] = 0
  AND a.[CustodyMode] = 0;
