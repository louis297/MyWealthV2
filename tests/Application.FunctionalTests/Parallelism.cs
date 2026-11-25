// Fixtures marked [Parallelizable] run at the same time as each other, each against its own
// database and test hosts (see TestEnvironmentPool). NUnit never runs them at the same time as
// fixtures without the attribute, which share the default database one at a time.
//
// Mark a new TestBase fixture [Parallelizable] unless it changes state that a database reset does
// not clear: the OpenIddict client rows (Respawn skips those tables), process-wide settings, or
// fixed paths on disk. Fixtures that do not derive from TestBase stay on the default environment.
//
// Each worker can hold one extra database and pair of test hosts. On a 36-core developer machine
// the full suite took 3m14s with 4 workers, 2m00s with 8 and 1m45s with 12, after which the longest
// fixture sets the pace. Pass `-- NUnit.NumberOfTestWorkers=12` to dotnet test to use more locally.
[assembly: LevelOfParallelism(16)]
