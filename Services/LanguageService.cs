using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AI_driven_teaching_platform.Models;

namespace AI_driven_teaching_platform.Services
{
    public class LanguageService
    {
        private readonly HttpClient _httpClient;
        private readonly Dictionary<string, SupportedLanguage> _supportedLanguages;

        public LanguageService(HttpClient httpClient)
        {
            _httpClient = httpClient;
            _supportedLanguages = InitializeSupportedLanguages();
        }

        public async Task<LanguageDetectionResult> DetectLanguageAsync(string text)
        {
            try
            {
                // Try OpenAI for language detection first
                var openaiResult = await DetectLanguageWithOpenAI(text);
                if (openaiResult != null) return openaiResult;

                // Fallback to rule-based detection
                return DetectLanguageRuleBased(text);
            }
            catch
            {
                return DetectLanguageRuleBased(text);
            }
        }

        public async Task<string> TranslateTextAsync(string text, string fromLanguage, string toLanguage)
        {
            try
            {
                // Try OpenAI for translation
                var openaiTranslation = await TranslateWithOpenAI(text, fromLanguage, toLanguage);
                if (!string.IsNullOrEmpty(openaiTranslation)) return openaiTranslation;

                // If OpenAI fails, return original text
                return text;
            }
            catch
            {
                return text;
            }
        }

        public List<SupportedLanguage> GetSupportedLanguages()
        {
            return _supportedLanguages.Values.ToList();
        }

        public bool IsLanguageSupported(string languageCode)
        {
            return _supportedLanguages.ContainsKey(languageCode.ToLower());
        }

        public string GetLanguageName(string languageCode)
        {
            return _supportedLanguages.TryGetValue(languageCode.ToLower(), out var language)
                ? language.Name : "Unknown";
        }

        // Private Methods
        private async Task<LanguageDetectionResult?> DetectLanguageWithOpenAI(string text)
        {
            try
            {
                var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (string.IsNullOrEmpty(openaiKey)) return null;

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", openaiKey);

                var prompt = $"Detect the language of this text and respond with just the ISO 639-1 language code (e.g., 'en', 'es', 'fr', 'hi', 'de'): \"{text}\"";

                var body = new
                {
                    model = "gpt-3.5-turbo",
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    max_tokens = 10,
                    temperature = 0.1
                };

                var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(result);
                    var detectedCode = doc.RootElement.GetProperty("choices")[0]
                        .GetProperty("message").GetProperty("content").GetString()?.Trim().ToLower();

                    if (!string.IsNullOrEmpty(detectedCode) && _supportedLanguages.ContainsKey(detectedCode))
                    {
                        return new LanguageDetectionResult
                        {
                            DetectedLanguage = detectedCode,
                            Confidence = 0.95,
                            LanguageName = _supportedLanguages[detectedCode].Name
                        };
                    }
                }
            }
            catch { }

            return null;
        }

        private async Task<string> TranslateWithOpenAI(string text, string fromLanguage, string toLanguage)
        {
            try
            {
                var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (string.IsNullOrEmpty(openaiKey)) return "";

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", openaiKey);

                var fromLangName = GetLanguageName(fromLanguage);
                var toLangName = GetLanguageName(toLanguage);

                var prompt = $"Translate this text from {fromLangName} to {toLangName}. Provide only the translation without any additional text:\n\n{text}";

                var body = new
                {
                    model = "gpt-3.5-turbo",
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    max_tokens = 1000,
                    temperature = 0.3
                };

                var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(result);
                    return doc.RootElement.GetProperty("choices")[0]
                        .GetProperty("message").GetProperty("content").GetString()?.Trim() ?? "";
                }
            }
            catch { }

            return "";
        }

        private LanguageDetectionResult DetectLanguageRuleBased(string text)
        {
            var textLower = text.ToLower();

            // Spanish detection
            if (ContainsSpanishPatterns(textLower))
            {
                return new LanguageDetectionResult
                {
                    DetectedLanguage = "es",
                    Confidence = 0.8,
                    LanguageName = "Spanish"
                };
            }

            // French detection
            if (ContainsFrenchPatterns(textLower))
            {
                return new LanguageDetectionResult
                {
                    DetectedLanguage = "fr",
                    Confidence = 0.8,
                    LanguageName = "French"
                };
            }

            // Hindi detection (basic)
            if (ContainsHindiPatterns(text))
            {
                return new LanguageDetectionResult
                {
                    DetectedLanguage = "hi",
                    Confidence = 0.9,
                    LanguageName = "Hindi"
                };
            }

            // German detection
            if (ContainsGermanPatterns(textLower))
            {
                return new LanguageDetectionResult
                {
                    DetectedLanguage = "de",
                    Confidence = 0.8,
                    LanguageName = "German"
                };
            }

            // Default to English
            return new LanguageDetectionResult
            {
                DetectedLanguage = "en",
                Confidence = 0.7,
                LanguageName = "English"
            };
        }

        private bool ContainsSpanishPatterns(string text)
        {
            var spanishWords = new[] { "qué", "cómo", "cuál", "dónde", "cuándo", "por qué", "es", "la", "el", "una", "uno", "¿", "ñ" };
            return spanishWords.Any(word => text.Contains(word));
        }

        private bool ContainsFrenchPatterns(string text)
        {
            var frenchWords = new[] { "qu'est-ce", "comment", "où", "quand", "pourquoi", "c'est", "le", "la", "les", "ç", "à", "è", "é" };
            return frenchWords.Any(word => text.Contains(word));
        }

        private bool ContainsHindiPatterns(string text)
        {
            // Basic Hindi/Devanagari script detection
            return text.Any(c => c >= '\u0900' && c <= '\u097F');
        }

        private bool ContainsGermanPatterns(string text)
        {
            var germanWords = new[] { "was", "wie", "wo", "wann", "warum", "ist", "das", "der", "die", "ein", "eine", "ä", "ö", "ü", "ß" };
            return germanWords.Any(word => text.Contains(word));
        }

        private Dictionary<string, SupportedLanguage> InitializeSupportedLanguages()
        {
            return new Dictionary<string, SupportedLanguage>
            {
                ["en"] = new SupportedLanguage { Code = "en", Name = "English", NativeName = "English", IsSupported = true },
                ["es"] = new SupportedLanguage { Code = "es", Name = "Spanish", NativeName = "Español", IsSupported = true },
                ["fr"] = new SupportedLanguage { Code = "fr", Name = "French", NativeName = "Français", IsSupported = true },
                ["hi"] = new SupportedLanguage { Code = "hi", Name = "Hindi", NativeName = "हिन्दी", IsSupported = true },
                ["de"] = new SupportedLanguage { Code = "de", Name = "German", NativeName = "Deutsch", IsSupported = true },
                ["pt"] = new SupportedLanguage { Code = "pt", Name = "Portuguese", NativeName = "Português", IsSupported = true },
                ["it"] = new SupportedLanguage { Code = "it", Name = "Italian", NativeName = "Italiano", IsSupported = true },
                ["zh"] = new SupportedLanguage { Code = "zh", Name = "Chinese", NativeName = "中文", IsSupported = true },
                ["ja"] = new SupportedLanguage { Code = "ja", Name = "Japanese", NativeName = "日本語", IsSupported = true },
                ["ko"] = new SupportedLanguage { Code = "ko", Name = "Korean", NativeName = "한국어", IsSupported = true }
            };
        }
    }
}
