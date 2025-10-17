// Program.cs
using System.Text.Json;
using Discord;
using Discord.WebSocket;

namespace NunuDiscordBot
{
    public static class Program
    {
        private static DiscordSocketClient? _client;

        public static async Task<int> Main(string[] args)
        {
            // Keep logs in output folder
            var logPath = Path.Combine(AppContext.BaseDirectory, "nunu-discord.log");
            using var logStream = new StreamWriter(File.Open(logPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            { AutoFlush = true };

            try
            {
                var cfgPath = ResolveConfigPath();
                var cfgJson = await File.ReadAllTextAsync(cfgPath);
                var cfg = JsonSerializer.Deserialize<NunuConfig>(cfgJson)
                          ?? throw new InvalidOperationException("Invalid appsettings.json (deserialization returned null).");

                // Token sanity check (Discord tokens are long; your placeholder isn’t)
                if (string.IsNullOrWhiteSpace(cfg.DiscordToken) || cfg.DiscordToken.StartsWith("PUT_", StringComparison.OrdinalIgnoreCase) || cfg.DiscordToken.Length < 50)
                {
                    WriteLine(logStream, "Config error: DiscordToken is missing or a placeholder.");
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("⚠ DiscordToken missing/placeholder.\nEdit appsettings.json and set a real bot token, then run again.");
                    Console.ResetColor();
                    PauseIfAttached();
                    return 2;
                }

                // Socket client
                _client = new DiscordSocketClient(new DiscordSocketConfig
                {
                    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
                    AlwaysDownloadUsers = false,
                    LogGatewayIntentWarnings = false
                });

                _client.Log += msg =>
                {
                    WriteLine(logStream, msg.ToString());
                    return Task.CompletedTask;
                };

                var nunu = new NunuCore(cfg);

                bool commandsRegistered = false;

                _client.Ready += async () =>
                {
                    try
                    {
                        WriteLine(logStream, $"Connected as {_client.CurrentUser}.");

                        if (commandsRegistered) return;

                        var cmd_nunu = new SlashCommandBuilder()
                            .WithName("nunu")
                            .WithDescription("Talk to Little Nunu")
                            .AddOption(new SlashCommandOptionBuilder()
                                .WithName("text")
                                .WithDescription("What to say")
                                .WithRequired(true)
                                .WithType(ApplicationCommandOptionType.String));

                        var cmd_song = new SlashCommandBuilder()
                            .WithName("song")
                            .WithDescription("Songcraft: compose a tiny MIDI idea")
                            .AddOption("mood", ApplicationCommandOptionType.String, "Mood tag (optional)", isRequired: false)
                            .AddOption("lyric", ApplicationCommandOptionType.String, "Text to inspire the melody", isRequired: true);

                        var cmd_remember = new SlashCommandBuilder()
                            .WithName("remember")
                            .WithDescription("Save a memory about a user/topic")
                            .AddOption("note", ApplicationCommandOptionType.String, "What should Nunu remember?", isRequired: true);

                        var cmd_forget = new SlashCommandBuilder()
                            .WithName("forget")
                            .WithDescription("Forget last memory or one containing a word")
                            .AddOption("filter", ApplicationCommandOptionType.String, "Optional word/phrase", isRequired: false);

                        var cmd_affinity = new SlashCommandBuilder()
                            .WithName("affinity")
                            .WithDescription("Show Nunu's current affinity towards you");

                        var cmd_emotion = new SlashCommandBuilder()
                            .WithName("emotion")
                            .WithDescription("Set/display Nunu’s emotion")
                            .AddOption("state", ApplicationCommandOptionType.String, "happy/sad/coy/fierce/serene/void", isRequired: false);

                        if (cfg.GuildId != 0)
                        {
                            var guild = _client.GetGuild(cfg.GuildId);
                            if (guild is null)
                            {
                                WriteLine(logStream, $"Warning: GuildId {cfg.GuildId} not found in cache; registering global instead.");
                                await _client.CreateGlobalApplicationCommandAsync(cmd_nunu.Build());
                                await _client.CreateGlobalApplicationCommandAsync(cmd_song.Build());
                                await _client.CreateGlobalApplicationCommandAsync(cmd_remember.Build());
                                await _client.CreateGlobalApplicationCommandAsync(cmd_forget.Build());
                                await _client.CreateGlobalApplicationCommandAsync(cmd_affinity.Build());
                                await _client.CreateGlobalApplicationCommandAsync(cmd_emotion.Build());
                            }
                            else
                            {
                                await guild.CreateApplicationCommandAsync(cmd_nunu.Build());
                                await guild.CreateApplicationCommandAsync(cmd_song.Build());
                                await guild.CreateApplicationCommandAsync(cmd_remember.Build());
                                await guild.CreateApplicationCommandAsync(cmd_forget.Build());
                                await guild.CreateApplicationCommandAsync(cmd_affinity.Build());
                                await guild.CreateApplicationCommandAsync(cmd_emotion.Build());
                                WriteLine(logStream, "Registered GUILD commands.");
                            }
                        }
                        else
                        {
                            await _client.CreateGlobalApplicationCommandAsync(cmd_nunu.Build());
                            await _client.CreateGlobalApplicationCommandAsync(cmd_song.Build());
                            await _client.CreateGlobalApplicationCommandAsync(cmd_remember.Build());
                            await _client.CreateGlobalApplicationCommandAsync(cmd_forget.Build());
                            await _client.CreateGlobalApplicationCommandAsync(cmd_affinity.Build());
                            await _client.CreateGlobalApplicationCommandAsync(cmd_emotion.Build());
                            WriteLine(logStream, "Registered GLOBAL commands.");
                        }

                        commandsRegistered = true;
                    }
                    catch (Exception ex)
                    {
                        WriteLine(logStream, "Exception in Ready: " + ex);
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Error during command registration. Check nunu-discord.log for details.");
                        Console.ResetColor();
                    }
                };

                _client.SlashCommandExecuted += async (SocketSlashCommand cmd) =>
                {
                    try
                    {
                        switch (cmd.Data.Name)
                        {
                            case "nunu":
                                {
                                    var text = (string)cmd.Data.Options.First().Value;
                                    await cmd.DeferAsync();
                                    await nunu.WithTyping(cmd.Channel, async () =>
                                    {
                                        var reply = await nunu.ChatAsync(cmd.User.Id, text);
                                        foreach (var chunk in Chunk(reply, 1800))
                                            await cmd.FollowupAsync(chunk);
                                    });
                                    break;
                                }

                            case "song":
                                {
                                    string? mood = (string?)cmd.Data.Options.FirstOrDefault(o => o.Name == "mood")?.Value;
                                    string lyric = (string)cmd.Data.Options.First(o => o.Name == "lyric").Value;
                                    await cmd.DeferAsync();
                                    await nunu.WithTyping(cmd.Channel, async () =>
                                    {
                                        var path = await nunu.SongAsync(lyric, mood);
                                        await cmd.FollowupAsync(path is null
                                            ? "My strings tangled—songcraft failed. WAH!"
                                            : $"Saved a tiny melody to `{path}`");
                                    });
                                    break;
                                }

                            case "remember":
                                {
                                    var note = (string)cmd.Data.Options.First().Value;
                                    nunu.Remember(cmd.User.Id, note);
                                    await cmd.RespondAsync("A silver thread tied to your soul—remembered.");
                                    break;
                                }

                            case "forget":
                                {
                                    string? filter = (string?)cmd.Data.Options.FirstOrDefault()?.Value;
                                    var count = nunu.Forget(cmd.User.Id, filter);
                                    await cmd.RespondAsync(count > 0
                                        ? $"Snipped {count} tangled thread(s)."
                                        : "Nothing there but dust and moonlight.");
                                    break;
                                }

                            case "affinity":
                                {
                                    var score = nunu.Affinity(cmd.User.Id);
                                    await cmd.RespondAsync($"Affinity shimmer: **{score:F2}**");
                                    break;
                                }

                            case "emotion":
                                {
                                    string? state = (string?)cmd.Data.Options.FirstOrDefault()?.Value;
                                    var now = nunu.Emotion(state);
                                    await cmd.RespondAsync($"Nunu feels **{now}**.");
                                    break;
                                }
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLine(logStream, "Exception in SlashCommandExecuted: " + ex);
                        if (!cmd.HasResponded)
                            await cmd.RespondAsync("Void-gremlin chewed a wire. Try again!");
                    }
                };

                // Graceful stop on Ctrl+C / close
                var done = new TaskCompletionSource();
                Console.CancelKeyPress += async (_, e) =>
                {
                    e.Cancel = true;
                    try
                    {
                        WriteLine(logStream, "Shutdown requested (Ctrl+C).");
                        if (_client is { } c) await c.LogoutAsync();
                        if (_client is { } c2) await c2.StopAsync();
                    }
                    catch (Exception ex)
                    {
                        WriteLine(logStream, "Exception during shutdown: " + ex);
                    }
                    done.TrySetResult();
                };

                await _client.LoginAsync(TokenType.Bot, cfg.DiscordToken);
                await _client.StartAsync();

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Nunu is awake on Discord. Press Ctrl+C to exit.");
                Console.ResetColor();

                // Keep alive until Ctrl+C or process exit
                await done.Task;
                return 0;
            }
            catch (Exception ex)
            {
                WriteLine(logStream, "Fatal: " + ex);
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Fatal error: " + ex.Message);
                Console.WriteLine("See nunu-discord.log for details.");
                Console.ResetColor();
                PauseIfAttached();
                return 1;
            }
        }

        private static void WriteLine(StreamWriter log, string line)
        {
            var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            log.WriteLine($"[{stamp}] {line}");
        }

        private static void PauseIfAttached()
        {
            // If launched by double-clicking, give time to read the message
            if (Environment.UserInteractive)
            {
                Console.WriteLine("Press Enter to exit.");
                Console.ReadLine();
            }
        }

        private static string ResolveConfigPath()
        {
            // 1) Output dir
            var baseDir = AppContext.BaseDirectory;
            var p1 = Path.Combine(baseDir, "appsettings.json");
            if (File.Exists(p1)) return p1;

            // 2) Working dir (VS)
            var p2 = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
            if (File.Exists(p2)) return p2;

            // 3) Create a default in output dir
            var json = """
            {
              "DiscordToken": "MTQyODYwMDAyMzc0MjE1Mjc2NA.GRZUSv.pkrgM2BjTD8_nYqEJ0j4-iZeiMIUTvjvi2fcQY",
              "GuildId": 0,
              "Model": "nunu-8b",
              "OllamaBaseUrl": "http://localhost:11434",
              "Temperature": 0.8,
              "Persona": "Nunubu “Nunu” Nubu — The Soul Weeper. Speak like a mischievous, void-touched Lalafell bard from FFXIV. Use occasional WAH!. Serious line: “Every note is a tether… every soul, a string.”",
              "MemoryPath": "memory.json"
            }
            """;
            File.WriteAllText(p1, json);
            return p1;
        }

        private static IEnumerable<string> Chunk(string s, int size)
        {
            for (int i = 0; i < s.Length; i += size)
                yield return s.Substring(i, Math.Min(size, s.Length - i));
        }
    }
}
