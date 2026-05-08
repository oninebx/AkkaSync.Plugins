using AkkaSync.Abstractions;
using AkkaSync.Abstractions.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SQLitePCL;
using System.Collections.Immutable;

namespace AkkaSync.Plugins.Sink.Sqlite
{
  public class SqliteSink : ISyncSink
  {

    private readonly string _connectionString;
    private static readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ILogger<SqliteSink> _logger;
    private static ImmutableHashSet<int> RECOVERABLE_ERROR_CODE =
    [
      raw.SQLITE_CONSTRAINT,
      raw.SQLITE_CONSTRAINT_NOTNULL,
      raw.SQLITE_CONSTRAINT_PRIMARYKEY,
      raw.SQLITE_CONSTRAINT_UNIQUE,
      raw.SQLITE_CONSTRAINT_FOREIGNKEY
    ];

    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string Key { get; init; }

    public SqliteSink(string connectionString, string pluginKey, ILogger<SqliteSink> logger)
    {
      _connectionString = connectionString;
      Key = pluginKey;
      _logger = logger;
      _logger.LogInformation("SqliteSink initialized with connection string: {ConnectionString}.", _connectionString);
    }
    public async Task<IReadOnlyList<ErrorContext>> WriteAsync(IEnumerable<TransformContext> contextBatch, CancellationToken cancellationToken)
    {

      var errors = new List<ErrorContext>();
      if (contextBatch == null || !contextBatch.Any())
      {
        errors.Add(new ErrorContext(Id, "No data detected to sink", "-1"));
        return errors;
      }
      await _writeLock.WaitAsync(cancellationToken);
      string tableName = string.Empty;
      try
      {
        using var connection = new SqliteConnection(_connectionString);

        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var (rows, rowErrors) = ExtractRows(contextBatch);
        errors.AddRange(rowErrors);


        foreach (var table in rows.GroupBy(r => r.Table))
        {
          var insertErrors = await InsertTableDataAsync(
              table.Key,
              [.. table],
              connection,
              transaction,
              cancellationToken);

          errors.AddRange(insertErrors);
        }

        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("Transaction committed successfully.");
        return errors;
      }
      catch (Exception ex)
      {
        _logger.LogError(ex, "Fatal error occurred while inserting rows into table {TableName}.", tableName);
        throw;
      }
      finally
      {
        _writeLock.Release();
      }

    }

    private (List<RowEnvelope> Rows, List<ErrorContext> Errors) ExtractRows(IEnumerable<TransformContext> contexts)
    {
      var rows = new List<RowEnvelope>();
      var errors = new List<ErrorContext>();

      foreach (var ctx in contexts)
      {
        if (ctx?.Artifacts == null) continue;

        foreach (var (key, value) in ctx.Artifacts)
        {
          if (value is Dictionary<string, object?> dict && dict.Count > 0)
          {
            rows.Add(new RowEnvelope(key, dict, ctx.Cursor));
          }
          else
          {
            errors.Add(new ErrorContext(
                Id,
                $"Invalid artifact for table '{key}'",
                ctx.Cursor.ToString()
            ));
          }
        }
      }

      return (rows, errors);
    }

    private async Task<IReadOnlyList<ErrorContext>> InsertTableDataAsync(
      string table,
      IReadOnlyList<RowEnvelope> rows,
      SqliteConnection connection,
      SqliteTransaction transaction,
      CancellationToken cancellationToken)
    {
      var errors = new List<ErrorContext>();
      if (rows.Count == 0)
      {
        _logger.LogInformation("Table {TableName} has no data, skipping.", table);
        return errors;
      }

      string Escape(string name) => $"\"{name.Replace("\"", "\"\"")}\"";
      var firstRow = rows.First();
      var tableName = Escape(table);
      var columns = firstRow.Data.Keys.ToList();
      var columnNames = string.Join(", ", columns.Select(Escape));
      var parameterNames = string.Join(", ", columns.Select(c => "@" + c));
      var insertStatement = $"INSERT INTO {tableName} ({columnNames}) VALUES ({parameterNames})";

      await using var cmd = new SqliteCommand(insertStatement, connection, transaction);
      foreach (var column in columns)
      {
        cmd.Parameters.Add(new SqliteParameter($"@{column}", DBNull.Value));
      }

      foreach (var row in rows)
      {
        foreach (var column in columns)
        {
          cmd.Parameters[$"@{column}"].Value = row.Data[column] ?? DBNull.Value;
        }
        try
        {
          await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (RECOVERABLE_ERROR_CODE.Contains(ex.SqliteErrorCode))
        {
          _logger.LogWarning(ex, "Recoverable error inserting row into {TableName}, skipping row.", tableName);
          errors.Add(new ErrorContext(Id, $"Recoverable error inserting row into {table}, skipping row.", row.Cursor));
          continue;
        }
      }
      _logger.LogInformation("Finished inserting rows into table {TableName}.", tableName);
      return errors;
    }

    private static string ExtractDbName(string connectionString)
    {
      if (string.IsNullOrWhiteSpace(connectionString))
        throw new ArgumentException("Connection string cannot be null or empty.");

      var builder = new SqliteConnectionStringBuilder(connectionString);

      var dataSource = builder.DataSource;

      if (string.IsNullOrWhiteSpace(dataSource))
        throw new InvalidOperationException("Data Source not found in connection string.");

      return Path.GetFileNameWithoutExtension(dataSource).ToLowerInvariant();
    }

    private static string NormalizePath(string path)
    {
      var fullPath = Path.GetFullPath(path);

      return fullPath
          .Replace("\\", "/")
          .ToLowerInvariant();
    }
  }
}

public sealed record RowEnvelope(string Table, Dictionary<string, object?> Data, string Cursor);
