namespace AI_driven_teaching_platform.Models
{
    public class HeyGenSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = "https://api.heygen.com/v2";
        public string DefaultAvatarId { get; set; } = "default";
        public int DefaultVideoLength { get; set; } = 60; // seconds
        public string DefaultVoice { get; set; } = "en-US-JennyNeural";
    }
}
