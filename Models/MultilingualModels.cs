namespace AI_driven_teaching_platform.Models
{
    public class MultilingualQuestionRequest
    {
        public string Question { get; set; } = string.Empty;
        public string? Language { get; set; } // Optional: "en", "es", "fr", "hi", etc.
        public bool AutoDetectLanguage { get; set; } = true;
        public bool TranslateResponse { get; set; } = false; // Translate response back to question language
    }

    public class LanguageDetectionResult
    {
        public string DetectedLanguage { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public string LanguageName { get; set; } = string.Empty;
    }

    public class MultilingualResponse
    {
        public string Question { get; set; } = string.Empty;
        public string QuestionLanguage { get; set; } = string.Empty;
        public string EnglishTranslation { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string AnswerLanguage { get; set; } = string.Empty;
        public string? TranslatedAnswer { get; set; }
        public List<string> ContextSources { get; set; } = new();
        public bool UsedFallback { get; set; }
        public DateTime ProcessedAt { get; set; }
    }

    public class SupportedLanguage
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string NativeName { get; set; } = string.Empty;
        public bool IsSupported { get; set; }
    }
}
