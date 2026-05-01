using AkkaSync.Abstractions;
using AkkaSync.Abstractions.Models;
using Microsoft.Extensions.Logging;

namespace AkkaSync.Plugins.Sources;

public class FolderWatcherSourceProvider : IPluginProvider<ISyncSource>
{
  public string Key => nameof(FolderWatcherSourceProvider);

  private readonly ILoggerFactory _factory;
  private readonly ISyncEnvironment _environment;

  public FolderWatcherSourceProvider(ISyncEnvironment environment, ILoggerFactory loggerFactory)
  {
    _factory = loggerFactory;
    _environment = environment;
  }

  public IEnumerable<ISyncSource> Create(PluginSpec context, CancellationToken cancellationToken)
  {
    if(!context.Parameters.TryGetProperty("source", out var sourceElement))
    {
      throw new InvalidOperationException("FolderWatcherSourceProvider requires a 'source' parameter.");
    }
    var extension = sourceElement.GetString();
    if (string.IsNullOrEmpty(extension))
    {
      throw new InvalidOperationException("FolderWatcherSourceProvider requires a valid 'source' parameter.");
    }
    if(!context.Parameters.TryGetProperty("folder", out var folderElement))
    {
      throw new InvalidOperationException("FolderWatcherSourceProvider requires a 'folder' parameter.");
    }
    var folderPath = folderElement.GetString();
    if (string.IsNullOrEmpty(folderPath)) {
      throw new InvalidOperationException("FolderWatcherSourceProvider requires a valid 'folder' parameter.");
    }
    var path = _environment.ResolvePath(folderPath) ?? folderPath;
    var files = Directory.GetFiles(path, $"*.{extension}");

    foreach (var file in files)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var name = Path.GetFileName(file);
      switch (extension)
      {
        case "csv":
          var csvlogger = _factory.CreateLogger<CsvSource>();
          yield return new CsvSource(file, context.Key, _environment, csvlogger);
          break;
        default:
          throw new NotSupportedException($"Source type {extension} is not supported.");
      }
    }
  }
}
