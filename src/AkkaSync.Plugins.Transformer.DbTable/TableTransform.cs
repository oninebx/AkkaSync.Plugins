using AkkaSync.Abstractions;
using AkkaSync.Abstractions.Models;

namespace AkkaSync.Plugins.Transform.DbTable;

public class TableTransform : ISyncTransform
{
  public string Produce { get; init; }
  public string[] DependsOn { get; init; }
  public string Id { get; init; } = Guid.NewGuid().ToString("N");

  public string Key { get; init; }

  private readonly string _table;
  private readonly Func<IReadOnlyDictionary<string, object?>, Dictionary<string, object?>> _mapRow;

  public TableTransform(
    string pluginKey,
    string table,
    string produce,
    string[] dependsOn,
    Func<IReadOnlyDictionary<string, object?>, Dictionary<string, object?>> mapRow)
  {
    Produce = produce;
    DependsOn = dependsOn;
    _mapRow = mapRow;
    _table = table;
    Key = pluginKey;
  }

  public Task<ErrorContext?> Transform(TransformContext context, CancellationToken cancellationToken)
  {
    try
    {
      var mappedRow = _mapRow(context.RawData);
      context.TryProduce(_table, mappedRow);
      return Task.FromResult<ErrorContext?>(null);
    }
    catch (Exception ex)
    {
      return Task.FromResult<ErrorContext?>(new ErrorContext(Id, ex.Message, context.Cursor));
    }
    
  }
}

