namespace RoamADB.Gateway.Diagnostics;

public static class ToolLocator
{
  public static string? FindAdb()
  {
    var candidates = new[]
    {
      Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Android", "Sdk", "platform-tools", "adb.exe"),
      Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe"),
      FindOnPath("adb.exe")
    };
    return candidates.FirstOrDefault(candidate => candidate is not null && File.Exists(candidate));
  }

  public static string? FindScrcpy()
  {
    var candidates = new[]
    {
      Path.Combine(AppContext.BaseDirectory, "scrcpy", "scrcpy.exe"),
      Path.Combine(AppContext.BaseDirectory, "scrcpy.exe"),
      FindOnPath("scrcpy.exe")
    };
    return candidates.FirstOrDefault(candidate => candidate is not null && File.Exists(candidate));
  }

  public static string? FindOnPath(string fileName)
  {
    var path = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(path))
    {
      return null;
    }

    foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
    {
      var cleaned = directory.Trim().Trim('"');
      if (string.IsNullOrWhiteSpace(cleaned))
      {
        continue;
      }

      try
      {
        var candidate = Path.Combine(cleaned, fileName);
        if (File.Exists(candidate))
        {
          return candidate;
        }
      }
      catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
      {
        // Ignore malformed PATH entries and continue to known locations.
      }
    }

    return null;
  }
}
