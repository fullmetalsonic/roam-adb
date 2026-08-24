namespace RoamADB.Gateway.Storage;

public sealed record GatewayPaths(string RootDirectory)
{
  public string DeviceRegistryPath => Path.Combine(RootDirectory, "devices.json");
  public string StatusPath => Path.Combine(RootDirectory, "status.json");

  public static GatewayPaths ForCurrentUser()
  {
    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    if (string.IsNullOrWhiteSpace(localAppData))
    {
      throw new InvalidOperationException("The current user local application-data folder is unavailable.");
    }

    return new GatewayPaths(Path.Combine(localAppData, "RoamADB", "Gateway"));
  }
}
