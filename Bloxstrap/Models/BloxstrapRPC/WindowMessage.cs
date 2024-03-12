namespace Bloxstrap.Models.BloxstrapRPC;

public class WindowMessage
{

    [JsonPropertyName("x")]
    public float? X { get; set; }

    [JsonPropertyName("y")]
    public float? Y { get; set; }

    [JsonPropertyName("width")]
    public float? Width { get; set; }
    
    [JsonPropertyName("height")]
    public float? Height { get; set; }

    [JsonPropertyName("scaleWidth")]
    public float? ScaleWidth { get; set; }

    [JsonPropertyName("scaleHeight")]
    public float? ScaleHeight { get; set; }

    [JsonPropertyName("reset")]
    public bool? Reset { get; set; }
}
