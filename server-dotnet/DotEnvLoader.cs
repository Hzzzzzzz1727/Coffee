using System.Text;

internal static class DotEnvLoader
{
    public static void Load()
    {
        foreach (var startDirectory in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, ".env");
                if (!File.Exists(path))
                    continue;

                LoadFile(path);
                return;
            }
        }
    }

    private static void LoadFile(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var lineNumber = 0;

        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            lineNumber++;
            var entry = line.Trim();
            if (entry.Length == 0 || entry.StartsWith('#'))
                continue;
            if (entry.StartsWith("export ", StringComparison.Ordinal))
                entry = entry[7..].TrimStart();

            var separator = entry.IndexOf('=');
            if (separator <= 0)
                throw new InvalidDataException($"Invalid .env entry at {path}:{lineNumber}; expected KEY=value.");

            var key = entry[..separator].Trim();
            if (!IsValidKey(key))
                throw new InvalidDataException($"Invalid .env key at {path}:{lineNumber}.");

            var value = ParseValue(entry[(separator + 1)..].Trim(), path, lineNumber);
            values[key] = value;
        }

        var applied = 0;
        foreach (var (key, value) in values)
        {
            if (Environment.GetEnvironmentVariable(key) is not null)
                continue;

            Environment.SetEnvironmentVariable(key, value);
            applied++;
        }

        Console.WriteLine($"Loaded {applied} environment value(s) from {path}; existing system values were preserved.");
    }

    private static string ParseValue(string value, string path, int lineNumber)
    {
        if (value.Length == 0)
            return string.Empty;

        if (value[0] is '"' or '\'')
        {
            var closingQuote = value.IndexOf(value[0], 1);
            if (closingQuote < 0)
                throw new InvalidDataException($"Unclosed quoted value in .env at {path}:{lineNumber}.");

            var suffix = value[(closingQuote + 1)..].Trim();
            if (suffix.Length > 0 && !suffix.StartsWith('#'))
                throw new InvalidDataException($"Unexpected text after quoted value in .env at {path}:{lineNumber}.");

            return value[1..closingQuote];
        }

        var commentStart = value.IndexOf(" #", StringComparison.Ordinal);
        if (commentStart >= 0)
            value = value[..commentStart];
        return value.TrimEnd();
    }

    private static bool IsValidKey(string key)
    {
        if (key.Length == 0 || !(char.IsAsciiLetter(key[0]) || key[0] == '_'))
            return false;

        return key.Skip(1).All(character => char.IsAsciiLetterOrDigit(character) || character == '_');
    }
}
