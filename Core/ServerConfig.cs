using System.Text.Json.Serialization;

namespace ScriptManagerPlugin
{
    public class ServerConfig
    {
        [JsonPropertyName("BASE_URL")]
        public string BaseUrl { get; set; }

        [JsonPropertyName("USERNAME")]
        public string Username { get; set; }

        [JsonPropertyName("PASSWORD")]
        public string Password { get; set; }
    }
}
