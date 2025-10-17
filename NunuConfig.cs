// NunuConfig.cs
namespace NunuDiscordBot;

public record NunuConfig(
    string DiscordToken,
    ulong GuildId,
    string Model,
    string OllamaBaseUrl,
    double Temperature,
    string Persona,
    string MemoryPath
);
