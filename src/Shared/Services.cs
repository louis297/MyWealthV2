namespace MyWealthV2.Shared;

public static class Services
{
    /// <summary>
    /// The name of the Web Frontend service.
    /// This service is responsible for hosting the frontend application.
    /// </summary>
    public const string WebFrontend = "webfrontend";

    /// <summary>
    /// The name of the Web API service.
    /// This service is responsible for hosting the Web API application.
    /// </summary>
    public const string WebApi = "webapi";

    /// <summary>
    /// Aspire resource name for src/IdentityHost (OpenIddict + hosted login).
    /// </summary>
    public const string Identity = "identity";

    /// <summary>
    /// Aspire resource name and OIDC client id for the Adviser Portal.
    /// </summary>
    public const string AdviserPortal = "adviser-portal";

    /// <summary>
    /// The name of the Database Server service.
    /// This service is responsible for hosting the database server (e.g., PostgreSQL, SQL Server, or SQLite).
    /// </summary>
    public const string DatabaseServer = "dbserver";

    /// <summary>
    /// The name of the Database.
    /// This is the name of the database that will be created and used by the application.
    /// </summary>
    public const string Database = "MyWealthDbV2";

    /// <summary>
    /// Aspire resource name for the SQL Server container used only by TestAppHost.
    /// Must not equal <see cref="DatabaseServer"/> so tests cannot replace the developer Persistent container.
    /// </summary>
    public const string TestDatabaseServer = "dbserver-test";

    /// <summary>
    /// Aspire resource name and SQL database name for TestAppHost.
    /// Application code still reads ConnectionStrings:MyWealthDbV2; tests inject the TestAppHost connection string under that key.
    /// </summary>
    public const string TestDatabase = "MyWealthDbV2-test";
}
