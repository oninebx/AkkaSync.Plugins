using AkkaSync.Abstractions;
using AkkaSync.Abstractions.Models;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace AkkaSync.Plugins.Sources;

public class CsvSource : ISyncSource
{

  private readonly string _filePath;
  private readonly char _delimiter;
  private readonly ILogger<CsvSource> _logger;
  private readonly Lazy<string> _id;
  private readonly Lazy<string> _etag;

  public CsvSource(string filePath, string pluginKey, ISyncEnvironment environment, ILogger<CsvSource> logger, char delimiter = ',')
  {
    _filePath = filePath;
    _delimiter = delimiter;
    _logger = logger;

    _id = new Lazy<string>(() =>
    {
      return environment.ComputeSha256(Type, QualifiedId);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    _etag = new Lazy<string>(() =>
    {
      var info = new FileInfo(_filePath);
      var input = $"{info.Length}:{info.LastWriteTimeUtc.Ticks}";

      return environment.ComputeSha256(info.Length.ToString(), info.LastWriteTimeUtc.Ticks.ToString());
    });
    Key = pluginKey;
  }

  public string Key { get; init; }

  public string Type => "CSV";

  public string Id => _id.Value;

  public string ETag => _etag.Value;

  //  Path.GetFullPath(_filePath).Replace('\\', '/').ToLowerInvariant();
  public string QualifiedId => $"csv-{Path.GetFileNameWithoutExtension(_filePath).ToLowerInvariant()}";

  public string Name => $"Extract from CSV";

  

  public async IAsyncEnumerable<(TransformContext? context, ErrorContext? error)> ReadAsync(string? cursor, [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    _ = int.TryParse(cursor, out int startRow);
    using var reader = new StreamReader(_filePath);
    string? headerLine = await reader.ReadLineAsync();
    if (headerLine == null)
    {
      yield break;
    }
    var headers = headerLine.Split(_delimiter);
    int index = 0;
    while (index < startRow && !reader.EndOfStream)
    {
      await reader.ReadLineAsync();
      index++;
    }
    while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
    {
      var line = await reader.ReadLineAsync(cancellationToken);
      index++;
      if (string.IsNullOrWhiteSpace(line))
      {
        yield return (null, new ErrorContext("Sources.CSVSource", "Data row cannot be empty or whitespace", index.ToString())
        {
          Context = JsonSerializer.Serialize(new
          {
            FilePath = _filePath,
            LineNumber = index,
          })
        });
        continue;
      }
      
      string[] values;

      TransformContext? context = null;
      ErrorContext? error = null;
      values = ParseCsvLine(line, _delimiter);
      if (values.Length != headers.Length)
      {
        error = new ErrorContext("source", $"CSV format error at line {index} in file {_filePath}: Column count does not match header", index.ToString());
      }
      else
      {
        var record = new Dictionary<string, object?>();
        for (int i = 0; i < headers.Length && i < values.Length; i++)
        {
          record[headers[i]] = values[i];
        }
        context = new TransformContext(record)
        {
          MetaData = new Dictionary<string, object>
          {
            ["SourceType"] = "CSV",
            ["FilePath"] = _filePath,
            ["LineNumber"] = index,
          },
          Cursor = index.ToString(),

        };
      }

      yield return (context, error);
    }
  }

  /// <summary>
  /// Parse a CSV line handling quoted fields correctly
  /// </summary>
  private string[] ParseCsvLine(string line, char delimiter)
  {
    var fields = new List<string>();
    var currentField = new System.Text.StringBuilder();
    bool insideQuotes = false;

    for (int i = 0; i < line.Length; i++)
    {
      char c = line[i];

      if (c == '"')
      {
        // Check for escaped quote (double quote)
        if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"')
        {
          currentField.Append('"');
          i++; // Skip the next quote
        }
        else
        {
          insideQuotes = !insideQuotes;
        }
      }
      else if (c == delimiter && !insideQuotes)
      {
        fields.Add(currentField.ToString());
        currentField.Clear();
      }
      else
      {
        currentField.Append(c);
      }
    }

    // Add the last field
    fields.Add(currentField.ToString());

    return [.. fields];
  }
}
