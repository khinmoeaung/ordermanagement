using System.Reflection;

using DbUp;
using DbUp.Engine;

namespace OrderManagement.Infrastructure.Persistence;

/// <summary>
/// Applies the embedded <c>database/migrations/*.sql</c> scripts with DbUp. Each script runs once
/// and is recorded in the <c>schemaversions</c> table.
/// </summary>
public static class DatabaseMigrator
{
    private const string ScriptPrefix = "OrderManagement.Migrations.";

    public static DatabaseUpgradeResult Migrate(string connectionString)
    {
        Assembly assembly = typeof(DatabaseMigrator).Assembly;

        DatabaseUpgradeResult result = DeployChanges.To
            .MySqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(assembly, name => name.StartsWith(ScriptPrefix, StringComparison.Ordinal))
            .LogToConsole()
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
        {
            throw new InvalidOperationException(
                $"Database migration failed on script '{result.ErrorScript?.Name}'.", result.Error);
        }

        return result;
    }
}
