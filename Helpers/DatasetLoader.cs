using System.Text.Json;
using AI_driven_teaching_platform.Models;
using Qdrant.Client.Grpc;

namespace AI_driven_teaching_platform.Helpers
{
    public class DatasetLoader
    {
        private readonly HttpClient _httpClient;
        private readonly Func<string, Task<float[]>> _generateEmbedding;

        public DatasetLoader(HttpClient httpClient, Func<string, Task<float[]>> generateEmbedding)
        {
            _httpClient = httpClient;
            _generateEmbedding = generateEmbedding;
        }

        public async Task<List<PointStruct>> LoadWikipediaEducationalContent()
        {
            var educationalTopics = new[]
            {
                // Biology
                ("Photosynthesis", "Biology", "Beginner"),
                ("Cell membrane", "Biology", "Intermediate"),
                ("DNA replication", "Biology", "Advanced"),
                ("Mitosis", "Biology", "Intermediate"),
                ("Ecosystem", "Biology", "Beginner"),
                ("Evolution", "Biology", "Advanced"),
                ("Protein synthesis", "Biology", "Advanced"),
                ("Respiration", "Biology", "Intermediate"),
                
                // Chemistry
                ("Chemical bond", "Chemistry", "Intermediate"),
                ("Periodic table", "Chemistry", "Beginner"),
                ("Organic chemistry", "Chemistry", "Advanced"),
                ("Acid-base reaction", "Chemistry", "Intermediate"),
                ("Oxidation", "Chemistry", "Intermediate"),
                ("Molecular orbital", "Chemistry", "Advanced"),
                
                // Physics
                ("Newton's laws of motion", "Physics", "Intermediate"),
                ("Electromagnetic radiation", "Physics", "Advanced"),
                ("Thermodynamics", "Physics", "Advanced"),
                ("Simple harmonic motion", "Physics", "Intermediate"),
                ("Quantum mechanics", "Physics", "Advanced"),
                ("Relativity", "Physics", "Advanced"),
                ("Wave-particle duality", "Physics", "Advanced"),
                
                // Mathematics
                ("Calculus", "Mathematics", "Advanced"),
                ("Linear algebra", "Mathematics", "Advanced"),
                ("Statistics", "Mathematics", "Intermediate"),
                ("Geometry", "Mathematics", "Beginner"),
                ("Trigonometry", "Mathematics", "Intermediate"),
                ("Differential equations", "Mathematics", "Advanced"),
                
                // History
                ("World War II", "History", "Intermediate"),
                ("Renaissance", "History", "Intermediate"),
                ("Industrial Revolution", "History", "Intermediate"),
                ("Cold War", "History", "Intermediate"),
                ("Ancient Rome", "History", "Beginner"),
                ("French Revolution", "History", "Intermediate"),
                
                // Computer Science
                ("Algorithm", "Computer Science", "Intermediate"),
                ("Data structure", "Computer Science", "Intermediate"),
                ("Machine learning", "Computer Science", "Advanced"),
                ("Database", "Computer Science", "Intermediate"),
                ("Operating system", "Computer Science", "Advanced"),
                
                // Geography
                ("Climate change", "Geography", "Intermediate"),
                ("Plate tectonics", "Geography", "Advanced"),
                ("Ecosystem", "Geography", "Intermediate"),
                ("Urbanization", "Geography", "Intermediate"),
                
                // Economics
                ("Supply and demand", "Economics", "Beginner"),
                ("Inflation", "Economics", "Intermediate"),
                ("Gross domestic product", "Economics", "Intermediate"),
                ("Market economy", "Economics", "Beginner")
            };

            var points = new List<PointStruct>();
            ulong id = 2000; // Start from 2000 for Wikipedia content

            foreach (var (topic, subject, difficulty) in educationalTopics)
            {
                try
                {
                    var content = await FetchWikipediaContent(topic);
                    if (!string.IsNullOrEmpty(content))
                    {
                        var embedding = await _generateEmbedding($"{topic} {content}");
                        var point = new PointStruct { Id = id++, Vectors = embedding };

                        point.Payload.Add("title", new Value { StringValue = topic });
                        point.Payload.Add("content", new Value { StringValue = content });
                        point.Payload.Add("subject", new Value { StringValue = subject });
                        point.Payload.Add("difficulty", new Value { StringValue = difficulty });
                        point.Payload.Add("source", new Value { StringValue = "Wikipedia" });
                        point.Payload.Add("created_at", new Value { StringValue = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });

                        points.Add(point);
                        Console.WriteLine($"Loaded: {topic} ({subject})");
                    }

                    await Task.Delay(200); // Rate limiting - respect Wikipedia's servers
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading {topic}: {ex.Message}");
                }
            }

            return points;
        }

        private async Task<string> FetchWikipediaContent(string topic)
        {
            try
            {
                var url = $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(topic)}";
                var response = await _httpClient.GetStringAsync(url);

                using var doc = JsonDocument.Parse(response);
                var extract = doc.RootElement.GetProperty("extract").GetString();

                // Clean and truncate content for embeddings
                var cleanContent = extract?.Replace("\n", " ").Trim();
                return cleanContent?.Length > 1500 ? cleanContent[..1500] + "..." : cleanContent ?? "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Wikipedia API error for {topic}: {ex.Message}");
                return "";
            }
        }
    }

    public class JsonDatasetLoader
    {
        public async Task<List<PointStruct>> LoadFromJsonFile(string filePath, Func<string, Task<float[]>> generateEmbedding)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Dataset file not found: {filePath}");

            var jsonContent = await File.ReadAllTextAsync(filePath);
            var dataset = JsonSerializer.Deserialize<EducationalDataset>(jsonContent);

            var points = new List<PointStruct>();
            ulong id = 3000; // Start from 3000 for JSON content

            if (dataset?.Subjects != null)
            {
                foreach (var subject in dataset.Subjects)
                {
                    foreach (var topic in subject.Topics)
                    {
                        var embedding = await generateEmbedding($"{topic.Title} {topic.Content}");
                        var point = new PointStruct { Id = id++, Vectors = embedding };

                        point.Payload.Add("title", new Value { StringValue = topic.Title });
                        point.Payload.Add("content", new Value { StringValue = topic.Content });
                        point.Payload.Add("subject", new Value { StringValue = subject.Name });
                        point.Payload.Add("difficulty", new Value { StringValue = topic.Difficulty });
                        point.Payload.Add("keywords", new Value { StringValue = string.Join(", ", topic.Keywords) });
                        point.Payload.Add("source", new Value { StringValue = "JSON Dataset" });
                        point.Payload.Add("created_at", new Value { StringValue = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });

                        points.Add(point);
                    }
                }
            }

            return points;
        }
    }
}
