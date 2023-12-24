namespace Bloxstrap.Models.BloxstrapRPC;

public class WindowTransparency
{

    [JsonPropertyName("transparency")]
    public float? Transparency { get; set; } = 0!;

    [JsonPropertyName("color")]
    public string? Color { get; set; } = null!;
}
