using AkkaSync.Abstractions;
using AkkaSync.Abstractions.Models;
using System.Text.Json;

namespace AkkaSync.Plugins.Transform.DbTable;

public class TableTransformProvider : IPluginProvider<ISyncTransform>
{
  public string Key => nameof(TableTransformProvider);
  private readonly ISyncEnvironment _environment;

  public TableTransformProvider(ISyncEnvironment environment)
  {
    _environment = environment;
  }

  public IEnumerable<ISyncTransform> Create(PluginSpec context, CancellationToken cancellationToken = default)
  {
    //var configFile = _environment.ResolvePath(context.Parameters.Get<string>("transformers"));
    //if (!File.Exists(configFile))
    //{
    //  throw new FileNotFoundException($"TableTransformerProvider config file not found: {configFile}");
    //}
    //var json = File.ReadAllText(configFile);

    var spec = context.Parameters.Deserialize<TableTransformSpec>(new JsonSerializerOptions
    {
      PropertyNameCaseInsensitive = true
    });
    if (spec == null)
    {
      throw new InvalidOperationException("TableTransformerProvider found no transformer specs in config file.");
    }
    yield return new TableTransform(
        context.Key,
        spec.Name,
        context.Key ?? string.Empty,
        context.DependsOn,
        row =>
        {
          var mapped = new Dictionary<string, object?>();
          for (int i = 0; i < spec.Fields.Length && i < spec.Columns.Length; i++)
          {
            var field = spec.Fields[i];
            var column = spec.Columns[i];
            if (row.TryGetValue(field, out var value))
            {
              mapped[column] = value;
            }
            else
            {
              mapped[column] = null;
            }
          }
          return mapped;
        });
  }
}