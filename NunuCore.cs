// NunuCore.cs
using Discord;
using Discord.WebSocket;
using System.Text;
using System.Text.Json;

namespace NunuDiscordBot
{
    public sealed class NunuCore
    {
        private readonly NunuConfig _cfg;
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };
        private readonly NunuMemory _mem;
        private readonly Random _rng = new();

        private string _emotion = "serene";

        public NunuCore(NunuConfig cfg)
        {
            _cfg = cfg;
            _mem = NunuMemory.Load(cfg.MemoryPath);
        }

        public async Task WithTyping(ISocketMessageChannel channel, Func<Task> work)
        {
            using var _ = channel.EnterTypingState();
            await work();
        }

        public void Remember(ulong userId, string note)
        {
            _mem.Append(userId, "note", note);
            _mem.Save(_cfg.MemoryPath);
        }

        public int Forget(ulong userId, string? filter)
        {
            var count = _mem.Forget(userId, filter);
            _mem.Save(_cfg.MemoryPath);
            return count;
        }

        public double Affinity(ulong userId)
        {
            var baseScore = _mem.Count(userId) * 0.2;
            return Math.Clamp(baseScore + _rng.NextDouble() * 0.2, 0, 10);
        }

        public string Emotion(string? set = null)
        {
            if (!string.IsNullOrWhiteSpace(set))
                _emotion = set.ToLowerInvariant();
            return _emotion;
        }

        public async Task<string> ChatAsync(ulong userId, string input)
        {
            var context = _mem.Last(userId, take: 6);
            var sys = $"{_cfg.Persona}\nCurrent emotion: {_emotion}.\nKeep responses under 1–3 short paragraphs. FFXIV tone.";

            var sb = new StringBuilder();
            sb.AppendLine($"system: {sys}");
            foreach (var (role, text) in context)
                sb.AppendLine($"{role}: {text}");
            sb.AppendLine($"user: {input}");

            var prompt = sb.ToString();
            var reply = await QueryOllamaAsync(prompt);

            _mem.Append(userId, "user", input);
            _mem.Append(userId, "assistant", reply);
            _mem.Save(_cfg.MemoryPath);
            return reply;
        }

        public async Task<string?> SongAsync(string lyric, string? mood)
        {
            var name = $"nunu_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}.mid";
            var path = Path.Combine(Environment.CurrentDirectory, name);
            await SimpleMidi.WriteSingleTrackAsync(path, lyric, mood ?? "neutral");
            return path;
        }

        private async Task<string> QueryOllamaAsync(string prompt)
        {
            var req = new
            {
                model = _cfg.Model,
                prompt,
                options = new { temperature = _cfg.Temperature }
            };

            using var msg = new HttpRequestMessage(HttpMethod.Post, $"{_cfg.OllamaBaseUrl}/api/generate");
            msg.Content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");

            using var res = await _http.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();

            using var s = await res.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(s);

            var sb = new StringBuilder();
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var obj = JsonSerializer.Deserialize<OllamaGen>(line);
                    if (obj?.response != null) sb.Append(obj.response);
                }
                catch { /* tolerate partial JSON frames */ }
            }
            return sb.ToString().Trim();
        }

        private sealed record OllamaGen(string? response, bool? done);
    }
}
