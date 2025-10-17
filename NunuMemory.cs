namespace NunuDiscordBot;

using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class NunuMemory
{
    public Dictionary<string, List<MemoryItem>> Store { get; set; } = new();

    public static NunuMemory Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<NunuMemory>(File.ReadAllText(path)) ?? new NunuMemory();
        }
        catch { }
        return new NunuMemory();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Append(ulong userId, string role, string text)
    {
        var key = userId.ToString();
        if (!Store.TryGetValue(key, out var list))
        {
            list = new List<MemoryItem>();
            Store[key] = list;
        }
        list.Add(new MemoryItem { Role = role, Text = text, When = DateTimeOffset.UtcNow });
        if (list.Count > 200) list.RemoveRange(0, list.Count - 200);
    }

    public IReadOnlyList<(string role, string text)> Last(ulong userId, int take)
    {
        var key = userId.ToString();
        if (!Store.TryGetValue(key, out var list)) return Array.Empty<(string, string)>();
        return list.TakeLast(take).Select(i => (i.Role, i.Text)).ToList();
    }

    public int Count(ulong userId)
    {
        var key = userId.ToString();
        return Store.TryGetValue(key, out var list) ? list.Count : 0;
    }

    public int Forget(ulong userId, string? filter)
    {
        var key = userId.ToString();
        if (!Store.TryGetValue(key, out var list)) return 0;
        if (string.IsNullOrWhiteSpace(filter))
        {
            var n = list.Count; list.Clear(); return n;
        }
        var before = list.Count;
        list.RemoveAll(i => i.Text.Contains(filter!, StringComparison.OrdinalIgnoreCase));
        return before - list.Count;
    }

    public sealed class MemoryItem
    {
        public string Role { get; set; } = "";
        public string Text { get; set; } = "";
        [JsonConverter(typeof(DateTimeOffsetConverter))]
        public DateTimeOffset When { get; set; }
    }

    private sealed class DateTimeOffsetConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => DateTimeOffset.Parse(reader.GetString()!);
        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString("o"));
    }
}
