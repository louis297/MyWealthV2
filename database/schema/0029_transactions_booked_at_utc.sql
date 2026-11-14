-- UTC instants (docs/fixes/utc-instants.md B5): every stored BookedAt holds offset +00:00.
-- SWITCHOFFSET keeps the instant; only the stored offset changes. Rows already at +00:00 are not touched.
-- The applicator runs this without a surrounding transaction; the single UPDATE is atomic and safe to rerun.
UPDATE [Transactions]
SET [BookedAt] = SWITCHOFFSET([BookedAt], '+00:00')
WHERE DATEPART(TZOFFSET, [BookedAt]) <> 0;
