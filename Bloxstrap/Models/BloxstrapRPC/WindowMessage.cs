namespace Bloxstrap.Models.BloxstrapRPC;

public class WindowMessage
{

    [JsonPropertyName("x")]
    public float? X { get; set; } = 0!;

    [JsonPropertyName("y")]
    public float? Y { get; set; } = 0!;

    [JsonPropertyName("width")]
    public float? Width { get; set; } = 0!;
    
    [JsonPropertyName("height")]
    public float? Height { get; set; } = 0!;

    [JsonPropertyName("scaleWidth")]
    public float? ScaleWidth { get; set; } = null!;

    [JsonPropertyName("scaleHeight")]
    public float? ScaleHeight { get; set; } = null!;
}
