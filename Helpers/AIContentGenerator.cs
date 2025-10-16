using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Qdrant.Client.Grpc;

namespace AI_driven_teaching_platform.Helpers
{
    public class AIContentGenerator
    {
        private readonly HttpClient _httpClient;
        private readonly string _openaiKey;

        public AIContentGenerator(HttpClient httpClient, string openaiKey)
        {
            _httpClient = httpClient;
            _openaiKey = openaiKey;
        }

        public async Task<List<PointStruct>> GenerateEducationalContent(Func<string, Task<float[]>> generateEmbedding)
        {
            var subjects = new[]
            {
                ("Biology", new[] { "Photosynthesis", "Cell Division", "Genetics", "Evolution", "Ecology", "Anatomy", "Biochemistry" }),
                ("Chemistry", new[] { "Atomic Structure", "Chemical Bonding", "Stoichiometry", "Thermochemistry", "Organic Reactions", "Electrochemistry" }),
                ("Physics", new[] { "Mechanics", "Electromagnetism", "Thermodynamics", "Quantum Physics", "Optics", "Nuclear Physics" }),
                ("Mathematics", new[] { "Calculus", "Linear Algebra", "Statistics", "Differential Equations", "Number Theory", "Graph Theory" }),
                ("History", new[] { "Ancient Civilizations", "Medieval Period", "Renaissance", "Industrial Revolution", "World Wars", "Modern Era" }),
                ("Computer Science", new[] { "Algorithms", "Data Structures", "Machine Learning", "Databases", "Networks", "Security" }),
                ("Geography", new[] { "Climate Systems", "Geological Processes", "Human Geography", "Cartography", "Environmental Science" }),
                ("Economics", new[] { "Microeconomics", "Macroeconomics", "International Trade", "Economic Policy", "Market Structures" })
            };

            var points = new List<PointStruct>();
            ulong id = 4000; // Start from 4000 for AI-generated content

            foreach (var (subject, topics) in subjects)
            {
                foreach (var topic in topics)
                {
                    try
                    {
                        var content = await GenerateTopicContent(subject, topic);
                        if (!string.IsNullOrEmpty(content))
                        {
                            var embedding = await generateEmbedding($"{topic} {content}");
                            var point = new PointStruct { Id = id++, Vectors = embedding };

                            point.Payload.Add("title", new Value { StringValue = topic });
                            point.Payload.Add("content", new Value { StringValue = content });
                            point.Payload.Add("subject", new Value { StringValue = subject });
                            point.Payload.Add("difficulty", new Value { StringValue = DetermineDifficulty(topic) });
                            point.Payload.Add("source", new Value { StringValue = "AI Generated" });
                            point.Payload.Add("created_at", new Value { StringValue = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });

                            points.Add(point);
                            Console.WriteLine($"Generated: {topic} ({subject})");
                        }

                        await Task.Delay(2000); // Rate limiting for OpenAI
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error generating content for {topic}: {ex.Message}");
                    }
                }
            }

            return points;
        }

        private async Task<string> GenerateTopicContent(string subject, string topic)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _openaiKey);

                var body = new
                {
                    model = "gpt-3.5-turbo",
                    messages = new object[]
                    {
                        new { role = "system", content = "You are an expert educator. Provide concise, accurate educational content for students. Focus on key concepts, definitions, and practical applications." },
                        new { role = "user", content = $"Explain {topic} in {subject} in 2-3 paragraphs suitable for students. Include key concepts, important details, and why it matters. Make it educational and engaging." }
                    },
                    max_tokens = 400,
                    temperature = 0.7
                };

                var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(result);
                    return doc.RootElement.GetProperty("choices")[0]
                        .GetProperty("message").GetProperty("content").GetString() ?? "";
                }

                return "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OpenAI API error for {topic}: {ex.Message}");
                return "";
            }
        }

        private string DetermineDifficulty(string topic)
        {
            var beginnerTopics = new[] { "Photosynthesis", "Atomic Structure", "Algebra", "Ancient Civilizations", "Supply and Demand" };
            var advancedTopics = new[] { "Quantum Physics", "Differential Equations", "Machine Learning", "Biochemistry", "Electrochemistry" };

            if (beginnerTopics.Any(t => topic.Contains(t, StringComparison.OrdinalIgnoreCase))) return "Beginner";
            if (advancedTopics.Any(t => topic.Contains(t, StringComparison.OrdinalIgnoreCase))) return "Advanced";
            return "Intermediate";
        }
    }
}
