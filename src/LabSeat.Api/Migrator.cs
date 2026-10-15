using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace LabSeat.Api;

/// <summary>
/// Applies the embedded db/*.sql scripts in name order, once each, splitting on GO lines.
/// Each script runs in its own transaction and is recorded in dbo.SchemaVersions.
/// </summary>
public static partial class Migrator
{
    private const string Prefix = "LabSeat.Db.";

    [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();

    public static IReadOnlyList<(string Name, string Sql)> Scripts()
    {
        var assembly = typeof(Migrator).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => (n[Prefix.Length..], Read(assembly, n)))
            .ToList();
    }

    public static IReadOnlyList<string> SplitBatches(string sql) =>
        GoSeparator().Split(sql).Select(b => b.Trim()).Where(b => b.Length > 0).ToList();

    /// <summary>Creates the database if it does not exist. Local development and tests only.</summary>
    public static async Task EnsureDatabaseAsync(string connectionString, CancellationToken ct = default)
    {
        var target = new SqlConnectionStringBuilder(connectionString);
        var database = target.InitialCatalog;
        if (string.IsNullOrEmpty(database))
        {
            throw new InvalidOperationException("The connection string has no Database/Initial Catalog.");
        }
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using var conn = new SqlConnection(master.ConnectionString);
        await conn.OpenAsync(ct);
        await using var command = new SqlCommand(
            "DECLARE @sql NVARCHAR(400) = N'CREATE DATABASE ' + QUOTENAME(@name); IF DB_ID(@name) IS NULL EXEC (@sql);", conn);
        command.Parameters.AddWithValue("@name", database);
        await command.ExecuteNonQueryAsync(ct);
    }

    public static async Task ApplyAsync(SqlConnectionFactory factory, ILogger logger, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenMaintenanceAsync(ct);
        await Execute(conn, null, """
            IF OBJECT_ID(N'dbo.SchemaVersions') IS NULL
                CREATE TABLE dbo.SchemaVersions (
                    ScriptName NVARCHAR(200) NOT NULL CONSTRAINT PK_SchemaVersions PRIMARY KEY,
                    AppliedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF_SchemaVersions_AppliedAt DEFAULT SYSUTCDATETIME());
            """, ct);

        foreach (var (name, sql) in Scripts())
        {
            await using var check = new SqlCommand("SELECT COUNT(*) FROM dbo.SchemaVersions WHERE ScriptName = @name", conn);
            check.Parameters.AddWithValue("@name", name);
            if ((int)(await check.ExecuteScalarAsync(ct))! > 0)
            {
                continue;
            }

            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
            foreach (var batch in SplitBatches(sql))
            {
                await Execute(conn, tx, batch, ct);
            }
            await using (var record = new SqlCommand("INSERT dbo.SchemaVersions (ScriptName) VALUES (@name)", conn, tx))
            {
                record.Parameters.AddWithValue("@name", name);
                await record.ExecuteNonQueryAsync(ct);
            }
            await tx.CommitAsync(ct);
            logger.LogInformation("Applied migration {Script}", name);
        }
    }

    private static async Task Execute(SqlConnection conn, SqlTransaction? tx, string sql, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, conn, tx) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
