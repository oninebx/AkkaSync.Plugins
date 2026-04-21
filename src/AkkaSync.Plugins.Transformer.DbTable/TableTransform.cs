using AkkaSync.Abstractions;
using AkkaSync.Abstractions.Models;

namespace AkkaSync.Plugins.Transform.DbTable;

public class TableTransform : ISyncTransform
{
  public string Produce { get; init; }
  public string[] DependsOn { get; init; }
  public string QualifiedId => $"database-table-{_table}";
  public string Name => $"Transform {_table}";

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

  public Task<TransformContext> Transform(TransformContext context, CancellationToken cancellationToken)
  {
    var mappedRow = _mapRow(context.RawData);
    context.TryProduce(_table, mappedRow);
    return Task.FromResult(context);
  }
}

