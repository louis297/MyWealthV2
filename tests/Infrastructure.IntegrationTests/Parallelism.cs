// Fixtures marked [Parallelizable] run at the same time as each other. Fixtures in the AppliedSchema namespace
// use the applied schema database of their test worker (SchemaDatabasePool), so fixtures that run at the
// same time never share one. NUnit never runs them at the same time as fixtures without the attribute.
//
// Mark a new AppliedSchema fixture [Parallelizable] unless it changes state outside its database:
// process-wide settings, environment variables or fixed paths on disk. Fixtures that call AddHangfire
// stay serial: it sets the process-wide JobStorage.Current, so two of them at once see each other's jobs.
[assembly: LevelOfParallelism(12)]
