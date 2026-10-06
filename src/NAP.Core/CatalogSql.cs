using Microsoft.Data.Sqlite;

namespace NAP.Core;

internal static class CatalogSql
{
    internal static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, params object?[] parameters)
    {
        var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        for (var i = 0; i < parameters.Length; i += 2) command.Parameters.AddWithValue((string)parameters[i]!, parameters[i + 1] ?? DBNull.Value);
        return command;
    }
    internal static void Execute(SqliteConnection c, SqliteTransaction? t, string sql, params object?[] p) { using var command = Command(c, t, sql, p); command.ExecuteNonQuery(); }
    internal static object? Scalar(SqliteConnection c, SqliteTransaction? t, string sql, params object?[] p) { using var command = Command(c, t, sql, p); return command.ExecuteScalar(); }
    internal static CatalogException Error(SqliteException error) => CatalogException.Stop(error.SqliteErrorCode switch
    {
        5 or 6 => NapIssueCodes.CatalogBusy,
        11 or 26 => NapIssueCodes.CatalogCorrupt,
        19 => NapIssueCodes.CatalogIntegrityFailed,
        _ => NapIssueCodes.CatalogInvalid
    }, "SQLite rejected the catalog operation; no success or overwrite is assumed.", inner: error);
}
