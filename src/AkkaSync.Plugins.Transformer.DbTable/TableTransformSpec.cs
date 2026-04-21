namespace AkkaSync.Plugins.Transform.DbTable;

public sealed record TableTransformSpec
{
  public string Name { get; init; } = string.Empty;
  //public string Type { get; init; } = string.Empty;
  public string[] Fields { get; init; } = [];
  public string[] Columns { get; init; } = [];
  //public string Produce { get; init; } = string.Empty;
  //public string[] DependsOn { get; init; } = [];
}