namespace Bloxstrap.Models.BloxstrapRPC;

public class WindowTitle
{

    [JsonPropertyName("Name")]
    public string? Name { get; set; } = ""!;
}
