// Fixtures marked [Parallelizable] run at the same time as each other, each against its own
// database (see TestEnvironmentPool). NUnit never runs them at the same time as fixtures without
// the attribute, which share the default database one at a time.
// Each parallel worker can hold one extra database and pair of test hosts, so keep this small.
[assembly: LevelOfParallelism(4)]
