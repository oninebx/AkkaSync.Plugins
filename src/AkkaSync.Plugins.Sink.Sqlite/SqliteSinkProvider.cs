using AkkaSync.Abstractions;
using AkkaSync.Abstractions.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AkkaSync.Plugins.Sink.Sqlite;

public class SqliteSinkProvider : IPluginProvider<ISyncSink>
{
  private readonly ILoggerFactory _factory;
  private readonly ISyncEnvironment _environment;
  public string Key => nameof(SqliteSinkProvider);

  public SqliteSinkProvider(ISyncEnvironment environment, ILoggerFactory factory)
  {
    _factory = factory;
    _environment = environment;
  }

  public IEnumerable<ISyncSink> Create(PluginSpec context, CancellationToken cancellationToken = default)
  {
    if (context.Parameters.TryGetProperty("connectionString", out var connectionElement))
    {
      var connectionString = connectionElement.GetString();
      if(string.IsNullOrEmpty(connectionString))
      {
        throw new NullReferenceException("Connection string cannot be empty.");
      }else
      {
        var qualifiedConnectionString = _environment.ResolveConnectionString(connectionString);

        yield return new SqliteSink(qualifiedConnectionString, context.Key, _factory.CreateLogger<SqliteSink>());
      }
      
    }

    
  }
}

public sealed record SqliteSinkSpec(string ConnectionString);
