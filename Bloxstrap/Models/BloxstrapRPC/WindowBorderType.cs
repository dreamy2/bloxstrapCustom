namespace Bloxstrap.Models.BloxstrapRPC;

public class WindowBorderType
{
    [JsonPropertyName("borderType")] // "windowed", "borderless", or "fullscreen"
    public string? BorderType { get; set; } = null!;
}
