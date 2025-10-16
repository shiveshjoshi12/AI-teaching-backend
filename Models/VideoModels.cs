namespace AI_driven_teaching_platform.Models
{
    // Request Models
    public class VideoQuestionRequest
    {
        public string Question { get; set; } = string.Empty;
        public string? AvatarId { get; set; }
        public string? Voice { get; set; }
        public string? Language { get; set; } = "en";
    }

    public class CreateVideoRequest
    {
        public string Text { get; set; } = string.Empty;
        public string AvatarId { get; set; } = "default";
        public string Voice { get; set; } = "en-US-JennyNeural";
        public string Title { get; set; } = "AI Teaching Video";
    }

    public class BatchVideoRequest
    {
        public List<VideoContent> Videos { get; set; } = new();
        public string AvatarId { get; set; } = "default";
        public string Voice { get; set; } = "en-US-JennyNeural";
    }

    public class VideoContent
    {
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    // Response Models
    public class VideoGenerationResponse
    {
        public string VideoId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? VideoUrl { get; set; }
        public string? ThumbnailUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? Duration { get; set; }
        public string AvatarUsed { get; set; } = string.Empty;
        public string TextContent { get; set; } = string.Empty;
    }

    public class VideoStatusResponse
    {
        public string VideoId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? VideoUrl { get; set; }
        public string? ThumbnailUrl { get; set; }
        public int Progress { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class AvatarInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public List<string> SupportedLanguages { get; set; } = new();
    }

    // HeyGen API Models (Internal)
    public class HeyGenVideoRequest
    {
        public string title { get; set; } = string.Empty;
        public HeyGenVideoConfig video_inputs { get; set; } = new();
        public HeyGenAspectRatio aspect_ratio { get; set; } = new() { width = 1920, height = 1080 };
    }

    public class HeyGenVideoConfig
    {
        public List<HeyGenCharacter> character { get; set; } = new();
        public HeyGenVoice voice { get; set; } = new();
        public HeyGenBackground background { get; set; } = new();
    }

    public class HeyGenCharacter
    {
        public string type { get; set; } = "avatar";
        public string avatar_id { get; set; } = string.Empty;
        public string avatar_style { get; set; } = "normal";
    }

    public class HeyGenVoice
    {
        public string type { get; set; } = "text";
        public string input_text { get; set; } = string.Empty;
        public string voice_id { get; set; } = string.Empty;
    }

    public class HeyGenBackground
    {
        public string type { get; set; } = "color";
        public string value { get; set; } = "#ffffff";
    }

    public class HeyGenAspectRatio
    {
        public int width { get; set; }
        public int height { get; set; }
    }

    public class HeyGenApiResponse
    {
        public bool error { get; set; }
        public HeyGenData data { get; set; } = new();
        public string message { get; set; } = string.Empty;
    }

    public class HeyGenData
    {
        public string video_id { get; set; } = string.Empty;
        public string status { get; set; } = string.Empty;
        public string? video_url { get; set; }
        public string? thumbnail_url { get; set; }
    }
}
