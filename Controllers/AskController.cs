using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AI_driven_teaching_platform.Models;
using AI_driven_teaching_platform.Helpers;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using AI_driven_teaching_platform.Services;
using AI_driven_teaching_platform.Data;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class AskController : ControllerBase
{
    private readonly HttpClient _httpClient;
    private readonly OpenRouterSettings _settings;

    public AskController(IHttpClientFactory httpClientFactory, IOptions<OpenRouterSettings> options)
    {
        _httpClient = httpClientFactory.CreateClient();
        _settings = options.Value;
    }

    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] QuestionRequest request)
    {
        if (string.IsNullOrEmpty(_settings.ApiKey))
            return BadRequest("OpenRouter API key not configured");

        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost:5000");
        _httpClient.DefaultRequestHeaders.Add("X-Title", "MyLearningPlatform");

        var body = new
        {
            model = "meta-llama/llama-3.3-8b-instruct:free",
            messages = new object[]
            {
                new { role = "system", content = "You are a helpful tutor." },
                new { role = "user", content = request.Question }
            }
        };

        var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync("https://openrouter.ai/api/v1/chat/completions", content);
            var responseString = await response.Content.ReadAsStringAsync();

            return response.IsSuccessStatusCode
                ? Content(responseString, "application/json")
                : StatusCode((int)response.StatusCode, $"OpenRouter API error: {response.StatusCode} - {responseString}");
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error calling OpenRouter API: {ex.Message}");
        }
    }

    [HttpGet("test-qdrant")]
    public async Task<IActionResult> TestQdrant()
    {
        try
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetStringAsync("http://localhost:6333/collections");
            return Ok(new { message = "Qdrant connection successful!", collections = response });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Qdrant connection failed: {ex.Message}");
        }
    }

    [HttpPost("setup-collection")]
    public async Task<IActionResult> SetupCollection()
    {
        try
        {
            var qdrantClient = new QdrantClient("localhost", 6334);

            // Check if collection exists
            try
            {
                await qdrantClient.GetCollectionInfoAsync("learning_content");
                return Ok(new { message = "Collection 'learning_content' already exists!" });
            }
            catch { /* Collection doesn't exist, create it */ }

            // Create collection
            await qdrantClient.CreateCollectionAsync("learning_content", new VectorParams
            {
                Size = 1536,
                Distance = Distance.Cosine
            });

            // Create basic sample points
            var sampleData = await CreateBasicSampleData();
            await qdrantClient.UpsertAsync("learning_content", sampleData);

            return Ok(new
            {
                message = "Collection created with basic sample data! Use /load-dataset for comprehensive content.",
                collection = "learning_content",
                count = sampleData.Length,
                recommendation = "Run POST /api/ask/load-dataset with {\"source\": \"wikipedia\"} for full educational content"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error setting up collection: {ex.Message}");
        }
    }

    [HttpPost("load-dataset")]
    public async Task<IActionResult> LoadDataset([FromBody] DatasetLoadRequest request)
    {
        try
        {
            var qdrantClient = new QdrantClient("localhost", 6334);
            var points = new List<PointStruct>();

            switch (request.Source.ToLower())
            {
                case "wikipedia":
                    var wikipediaLoader = new DatasetLoader(_httpClient, GenerateEmbedding);
                    points = await wikipediaLoader.LoadWikipediaEducationalContent();
                    break;

                case "json":
                    if (string.IsNullOrEmpty(request.FilePath))
                        return BadRequest("FilePath required for JSON source");
                    var jsonLoader = new JsonDatasetLoader();
                    points = await jsonLoader.LoadFromJsonFile(request.FilePath, GenerateEmbedding);
                    break;

                case "ai-generated":
                    var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                    if (string.IsNullOrEmpty(openaiKey))
                        return BadRequest("OPENAI_API_KEY environment variable required for AI-generated content");
                    var aiGenerator = new AIContentGenerator(_httpClient, openaiKey);
                    points = await aiGenerator.GenerateEducationalContent(GenerateEmbedding);
                    break;

                case "comprehensive":
                    // Load from multiple sources
                    points = await LoadComprehensiveDataset();
                    break;

                default:
                    return BadRequest("Supported sources: wikipedia, json, ai-generated, comprehensive");
            }

            if (points.Any())
            {
                // Process in batches to avoid overwhelming Qdrant
                var batchSize = 50;
                for (int i = 0; i < points.Count; i += batchSize)
                {
                    var batch = points.Skip(i).Take(batchSize).ToList();
                    await qdrantClient.UpsertAsync("learning_content", batch);
                    await Task.Delay(500); // Small delay between batches
                }
            }

            return Ok(new
            {
                message = $"Successfully loaded {points.Count} educational topics from {request.Source}",
                source = request.Source,
                total_points = points.Count,
                subjects = points.Select(p => p.Payload.ContainsKey("subject") ? p.Payload["subject"].StringValue : "Unknown").Distinct().ToList(),
                processing_time = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error loading dataset: {ex.Message}");
        }
    }

    [HttpPost("add-content")]
    public async Task<IActionResult> AddContent([FromBody] ContentRequest request)
    {
        try
        {
            var qdrantClient = new QdrantClient("localhost", 6334);

            var embedding = await GenerateEmbedding($"{request.Title} {request.Content}");
            var pointId = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var point = new PointStruct { Id = pointId, Vectors = embedding };
            point.Payload.Add("title", new Value { StringValue = request.Title });
            point.Payload.Add("content", new Value { StringValue = request.Content });
            point.Payload.Add("subject", new Value { StringValue = request.Subject });
            point.Payload.Add("difficulty", new Value { StringValue = request.Difficulty });
            point.Payload.Add("source", new Value { StringValue = "Manual" });
            point.Payload.Add("created_at", new Value { StringValue = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });

            await qdrantClient.UpsertAsync("learning_content", new[] { point });

            return Ok(new
            {
                message = "Content added successfully!",
                id = pointId,
                title = request.Title,
                subject = request.Subject
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error adding content: {ex.Message}");
        }
    }

    [HttpPost("bulk-upload")]
    public async Task<IActionResult> BulkUploadContent([FromBody] List<ContentRequest> contents)
    {
        try
        {
            var qdrantClient = new QdrantClient("localhost", 6334);
            var points = new List<PointStruct>();

            foreach (var content in contents)
            {
                var embedding = await GenerateEmbedding($"{content.Title} {content.Content}");
                var pointId = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (ulong)points.Count;

                var point = new PointStruct { Id = pointId, Vectors = embedding };
                point.Payload.Add("title", new Value { StringValue = content.Title });
                point.Payload.Add("content", new Value { StringValue = content.Content });
                point.Payload.Add("subject", new Value { StringValue = content.Subject });
                point.Payload.Add("difficulty", new Value { StringValue = content.Difficulty });
                point.Payload.Add("source", new Value { StringValue = "Bulk Upload" });
                point.Payload.Add("created_at", new Value { StringValue = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });

                points.Add(point);
            }

            await qdrantClient.UpsertAsync("learning_content", points);

            return Ok(new
            {
                message = "Bulk content uploaded successfully!",
                count = points.Count,
                subjects = contents.Select(c => c.Subject).Distinct().ToList()
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error bulk uploading content: {ex.Message}");
        }
    }

    [HttpPost("search")]
    public async Task<IActionResult> Search([FromBody] SearchRequest request)
    {
        try
        {
            var qdrantClient = new QdrantClient("localhost", 6334);

            var queryVector = await GenerateEmbedding(request.Query);
            var searchResult = await qdrantClient.SearchAsync("learning_content", queryVector, limit: 10);

            var results = searchResult.Select(result => new
            {
                id = result.Id,
                score = result.Score,
                title = result.Payload["title"].StringValue,
                content = result.Payload["content"].StringValue,
                subject = result.Payload.ContainsKey("subject") ? result.Payload["subject"].StringValue : "Unknown",
                difficulty = result.Payload.ContainsKey("difficulty") ? result.Payload["difficulty"].StringValue : "Unknown",
                source = result.Payload.ContainsKey("source") ? result.Payload["source"].StringValue : "Unknown",
                relevance = result.Score > 0.8 ? "Very High" : result.Score > 0.6 ? "High" : result.Score > 0.4 ? "Medium" : "Low"
            });

            return Ok(new
            {
                query = request.Query,
                results,
                total_found = results.Count()
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error searching: {ex.Message}");
        }
    }

    [HttpPost("smart-ask")]
    public async Task<IActionResult> SmartAsk(
     [FromBody] QuestionRequest request,
     [FromServices] IChatService chatService,
     [FromServices] ApplicationDbContext dbContext)
    {
        try
        {
            // ✅ Get or create test user
            var testUser = await dbContext.Users
                .FirstOrDefaultAsync(u => u.Email == "test@example.com");

            if (testUser == null)
            {
                testUser = new User
                {
                    Id = Guid.NewGuid().ToString(),
                    GoogleId = "test-google-id",
                    Email = "test@example.com",
                    Name = "Test User",
                    Picture = "https://lh3.googleusercontent.com/a/default-user",
                    Role = "Student",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    LastLoginAt = DateTime.UtcNow
                };

                dbContext.Users.Add(testUser);
                await dbContext.SaveChangesAsync();
            }

            var userId = testUser.Id;  // ✅ Use this instead of "anonymous-user"

            // Create or get active chat session
            var session = await chatService.CreateOrGetSessionAsync(userId, "Chat Session");


            // Step 1: Search Qdrant for relevant content
            using var searchHttpClient = new HttpClient();
            var queryEmbedding = await GenerateEmbedding(request.Question);

            var searchBody = new
            {
                vector = queryEmbedding,
                limit = 5,
                with_payload = true,
                score_threshold = 0.2
            };

            var searchContent = new StringContent(
                JsonSerializer.Serialize(searchBody),
                Encoding.UTF8,
                "application/json");

            var searchResponse = await searchHttpClient.PostAsync(
                "http://localhost:6333/collections/learning_content/points/search",
                searchContent);

            var searchResultJson = await searchResponse.Content.ReadAsStringAsync();

            // Step 2: Extract context
            var context = ExtractEnhancedContext(searchResultJson, request.Question);
            var searchScore = ExtractBestScore(searchResultJson);

            // Step 3: Generate AI response
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost:5000");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "MyLearningPlatform");

            var systemPrompt = context.Contains("No highly relevant context")
                ? GenerateEducationalFallbackPrompt(request.Question)
                : $"You are an expert educational tutor. Use this context to provide accurate, detailed educational answers. Explain concepts clearly and provide examples when helpful.\n\nContext: {context}";

            var chatBody = new
            {
                model = "meta-llama/llama-3.3-8b-instruct:free",
                messages = new object[]
                {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = request.Question }
                }
            };

            var chatContent = new StringContent(
                JsonSerializer.Serialize(chatBody),
                Encoding.UTF8,
                "application/json");

            var chatResponse = await _httpClient.PostAsync(
                "https://openrouter.ai/api/v1/chat/completions",
                chatContent);

            var chatResult = await chatResponse.Content.ReadAsStringAsync();

            // Parse the AI response
            var aiAnswer = ExtractAIResponseText(chatResult);

            // ✅ SAVE TO DATABASE
            await chatService.SaveMessageAsync(
                sessionId: session.Id,
                question: request.Question,
                answer: aiAnswer,
                questionLanguage: "auto",  // Changed from request.Language
                answerLanguage: "auto",    // Changed from request.Language
                usedRAG: !context.Contains("No highly relevant context"),
                searchScore: searchScore
            );

            return Ok(new
            {
                question = request.Question,
                answer = aiAnswer,  // Return parsed answer instead of raw JSON
                context_used = context,
                session_id = session.Id,  // Return session ID for frontend
                search_metadata = new
                {
                    embedding_dimensions = queryEmbedding.Length,
                    search_results_found = ExtractResultCount(searchResultJson),
                    context_type = context.Contains("No highly relevant context") ? "General Knowledge" : "Specific Context",
                    search_timestamp = DateTime.UtcNow,
                    saved_to_database = true
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = $"Error in smart ask: {ex.Message}",
                answer = "Sorry, I encountered an error."
            });
        }
    }

    // Helper method to extract best search score
    private double? ExtractBestScore(string searchResultJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(searchResultJson);
            if (doc.RootElement.TryGetProperty("result", out var result) && result.GetArrayLength() > 0)
            {
                return result[0].GetProperty("score").GetDouble();
            }
        }
        catch { }
        return null;
    }


    [HttpGet("subjects")]
    public async Task<IActionResult> GetSubjects()
    {
        try
        {
            using var httpClient = new HttpClient();
            var response = await httpClient.GetStringAsync("http://localhost:6333/collections/learning_content/points/scroll?limit=1000");

            using var doc = JsonDocument.Parse(response);
            var subjects = new Dictionary<string, int>();

            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.TryGetProperty("points", out var points))
            {
                foreach (var point in points.EnumerateArray())
                {
                    if (point.TryGetProperty("payload", out var payload) &&
                        payload.TryGetProperty("subject", out var subject))
                    {
                        var subjectName = subject.GetString() ?? "Unknown";
                        subjects[subjectName] = subjects.ContainsKey(subjectName) ? subjects[subjectName] + 1 : 1;
                    }
                }
            }

            return Ok(new
            {
                subjects = subjects.OrderBy(s => s.Key).ToDictionary(x => x.Key, x => x.Value),
                total_subjects = subjects.Count,
                total_content = subjects.Values.Sum()
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error getting subjects: {ex.Message}");
        }
    }

    [HttpPost("embedding")]
    public async Task<IActionResult> GetEmbedding([FromBody] EmbeddingRequest request)
    {
        try
        {
            var embedding = await GenerateEmbedding(request.Text);
            return Ok(new
            {
                text = request.Text,
                embedding = embedding,
                dimensions = embedding.Length,
                model_used = "text-embedding-3-small"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error generating embedding: {ex.Message}");
        }
    }

    // Helper Methods
    private async Task<float[]> GenerateEmbedding(string text)
    {
        try
        {
            using var client = new HttpClient();
            var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

            if (string.IsNullOrEmpty(openaiKey))
            {
                return GenerateDummyVector(1536);
            }

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", openaiKey);

            var body = new
            {
                model = "text-embedding-3-small",
                input = text.Length > 8192 ? text[..8192] : text
            };

            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://api.openai.com/v1/embeddings", content);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(result);
                return doc.RootElement.GetProperty("data")[0]
                    .GetProperty("embedding").EnumerateArray()
                    .Select(x => (float)x.GetDouble()).ToArray();
            }

            return GenerateDummyVector(1536);
        }
        catch
        {
            return GenerateDummyVector(1536);
        }
    }

    private async Task<List<PointStruct>> LoadComprehensiveDataset()
    {
        var allPoints = new List<PointStruct>();

        try
        {
            // Load Wikipedia content
            var wikipediaLoader = new DatasetLoader(_httpClient, GenerateEmbedding);
            var wikipediaPoints = await wikipediaLoader.LoadWikipediaEducationalContent();
            allPoints.AddRange(wikipediaPoints);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading Wikipedia content: {ex.Message}");
        }

        // Add built-in comprehensive content
        var builtInContent = await CreateComprehensiveBuiltInContent();
        allPoints.AddRange(builtInContent);

        return allPoints;
    }

    private async Task<PointStruct[]> CreateBasicSampleData()
    {
        var basicContents = new[]
        {
            ("Machine Learning", "Machine learning is a subset of artificial intelligence that focuses on algorithms learning from data.", "Computer Science", "Intermediate"),
            ("Photosynthesis", "Photosynthesis is the process by which plants convert light energy into chemical energy using carbon dioxide and water.", "Biology", "Beginner"),
            ("Chemical Bonds", "Chemical bonds form when atoms share or transfer electrons to achieve stable electron configurations.", "Chemistry", "Intermediate"),
            ("Newton's Laws", "Newton's three laws of motion describe the relationship between forces acting on objects and their motion.", "Physics", "Intermediate")
        };

        var points = new List<PointStruct>();
        ulong id = 1;

        foreach (var (title, content, subject, difficulty) in basicContents)
        {
            var embedding = await GenerateEmbedding($"{title} {content}");
            var point = new PointStruct { Id = id++, Vectors = embedding };

            point.Payload.Add("title", new Value { StringValue = title });
            point.Payload.Add("content", new Value { StringValue = content });
            point.Payload.Add("subject", new Value { StringValue = subject });
            point.Payload.Add("difficulty", new Value { StringValue = difficulty });
            point.Payload.Add("source", new Value { StringValue = "Basic Sample" });
            point.Payload.Add("created_at", new Value { StringValue = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });

            points.Add(point);
        }

        return points.ToArray();
    }

    private async Task<List<PointStruct>> CreateComprehensiveBuiltInContent()
    {
        var comprehensiveContent = new[]
        {
            // Biology - Expanded
            ("Cell Structure", "Cells are the basic units of life, containing organelles like nucleus (controls cell activities), mitochondria (produces energy), and ribosomes (synthesize proteins).", "Biology", "Beginner"),
            ("DNA and Genetics", "DNA contains genetic instructions for all living organisms. Genes are segments of DNA that code for specific traits through protein synthesis.", "Biology", "Intermediate"),
            ("Evolution", "Evolution is the process by which species change over time through natural selection, genetic drift, and other mechanisms.", "Biology", "Advanced"),
            ("Ecosystems", "Ecosystems are communities of living organisms interacting with their physical environment through energy flow and nutrient cycling.", "Biology", "Intermediate"),
            
            // Chemistry - Expanded
            ("Periodic Table", "The periodic table organizes elements by atomic number, revealing patterns in properties like electron configuration and chemical reactivity.", "Chemistry", "Beginner"),
            ("Organic Chemistry", "Organic chemistry studies carbon-containing compounds, which form the basis of all living organisms and many synthetic materials.", "Chemistry", "Advanced"),
            ("Acids and Bases", "Acids are proton donors while bases are proton acceptors. Their interactions determine pH and are crucial in biological systems.", "Chemistry", "Intermediate"),
            
            // Physics - Expanded  
            ("Electromagnetic Waves", "Electromagnetic waves are energy patterns that travel through space, including visible light, radio waves, and X-rays.", "Physics", "Advanced"),
            ("Thermodynamics", "Thermodynamics studies heat, work, and energy transfer, governing everything from engines to biological processes.", "Physics", "Advanced"),
            ("Simple Harmonic Motion", "Simple harmonic motion describes repetitive oscillations like pendulums and springs, fundamental to understanding waves.", "Physics", "Intermediate"),
            
            // Mathematics - Expanded
            ("Calculus", "Calculus studies rates of change (derivatives) and accumulation (integrals), essential for physics and engineering applications.", "Mathematics", "Advanced"),
            ("Statistics", "Statistics involves collecting, analyzing, and interpreting numerical data to make informed decisions and predictions.", "Mathematics", "Intermediate"),
            ("Geometry", "Geometry studies shapes, sizes, and spatial relationships, forming the foundation for architecture and computer graphics.", "Mathematics", "Beginner"),
            ("Linear Algebra", "Linear algebra deals with vectors and matrices, crucial for computer graphics, machine learning, and engineering.", "Mathematics", "Advanced"),
            
            // History - Expanded
            ("World War II", "World War II (1939-1945) was a global conflict that reshaped international relations and accelerated technological development.", "History", "Intermediate"),
            ("Renaissance", "The Renaissance (14th-17th centuries) marked a cultural rebirth in Europe, emphasizing art, science, and humanist philosophy.", "History", "Intermediate"),
            ("Industrial Revolution", "The Industrial Revolution transformed society through mechanization, urbanization, and mass production from 1760-1840.", "History", "Intermediate"),
            
            // Computer Science - Expanded
            ("Algorithms", "Algorithms are step-by-step procedures for solving problems, fundamental to computer programming and efficiency.", "Computer Science", "Intermediate"),
            ("Data Structures", "Data structures organize and store data efficiently, including arrays, trees, and hash tables for optimal performance.", "Computer Science", "Intermediate"),
            ("Databases", "Databases store and manage structured information, using SQL queries and normalization for data integrity.", "Computer Science", "Advanced"),
            ("Artificial Intelligence", "AI creates systems that can perform tasks typically requiring human intelligence, including learning and decision-making.", "Computer Science", "Advanced"),
            
            // Literature - New
            ("Shakespeare", "William Shakespeare revolutionized English literature with complex characters and timeless themes in plays like Hamlet and Romeo and Juliet.", "Literature", "Intermediate"),
            ("Poetry Analysis", "Poetry uses literary devices like metaphor, rhythm, and imagery to convey emotions and ideas in concentrated artistic language.", "Literature", "Beginner"),
            
            // Geography - New
            ("Climate Change", "Climate change refers to long-term shifts in global weather patterns, primarily caused by human activities increasing greenhouse gases.", "Geography", "Intermediate"),
            ("Plate Tectonics", "Plate tectonics explains Earth's surface movements through the interaction of lithospheric plates, causing earthquakes and mountain formation.", "Geography", "Advanced"),
            
            // Economics - New
            ("Supply and Demand", "Supply and demand are fundamental economic forces that determine prices and resource allocation in market economies.", "Economics", "Beginner"),
            ("Inflation", "Inflation is the general increase in prices over time, affecting purchasing power and economic stability.", "Economics", "Intermediate")
        };

        var points = new List<PointStruct>();
        ulong id = 1000; // Start from 1000 to avoid conflicts

        foreach (var (title, content, subject, difficulty) in comprehensiveContent)
        {
            var embedding = await GenerateEmbedding($"{title} {content}");
            var point = new PointStruct { Id = id++, Vectors = embedding };

            point.Payload.Add("title", new Value { StringValue = title });
            point.Payload.Add("content", new Value { StringValue = content });
            point.Payload.Add("subject", new Value { StringValue = subject });
            point.Payload.Add("difficulty", new Value { StringValue = difficulty });
            point.Payload.Add("source", new Value { StringValue = "Comprehensive Built-in" });
            point.Payload.Add("created_at", new Value { StringValue = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });

            points.Add(point);
        }

        return points;
    }

    private string ExtractEnhancedContext(string searchResultJson, string question)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(searchResultJson);

            if (!document.RootElement.TryGetProperty("result", out var resultElement) ||
                resultElement.GetArrayLength() == 0)
            {
                return "No highly relevant context found. The AI will provide general educational knowledge.";
            }

            var contextParts = new List<string>();
            var questionLower = question.ToLower();

            foreach (var item in resultElement.EnumerateArray())
            {
                if (item.TryGetProperty("payload", out var payload) &&
                    item.TryGetProperty("score", out var scoreElement))
                {
                    var score = scoreElement.GetDouble();
                    var title = payload.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : "Untitled";
                    var content = payload.TryGetProperty("content", out var contentProp) ? contentProp.GetString() : "";
                    var subject = payload.TryGetProperty("subject", out var subjectProp) ? subjectProp.GetString() : "";
                    var difficulty = payload.TryGetProperty("difficulty", out var diffProp) ? diffProp.GetString() : "";
                    var source = payload.TryGetProperty("source", out var srcProp) ? srcProp.GetString() : "";

                    if (!string.IsNullOrEmpty(content) && score > 0.2)
                    {
                        contextParts.Add($"[{subject} - {difficulty}] {title}: {content} (Source: {source}, Relevance: {score:F2})");
                    }
                }
            }

            return contextParts.Count > 0
                ? string.Join("\n\n", contextParts)
                : "No highly relevant context found. The AI will provide general educational knowledge.";
        }
        catch (Exception ex)
        {
            return $"Error processing context: {ex.Message}";
        }
    }

    private string GenerateEducationalFallbackPrompt(string question)
    {
        var questionLower = question.ToLower();

        if (questionLower.Contains("photosynthesis") || questionLower.Contains("plant") || questionLower.Contains("biology"))
        {
            return "You are an expert biology tutor. The student is asking about biological concepts. Provide a comprehensive, educational answer with clear explanations and examples.";
        }
        else if (questionLower.Contains("chemistry") || questionLower.Contains("chemical") || questionLower.Contains("molecule"))
        {
            return "You are an expert chemistry tutor. The student is asking about chemical concepts. Provide a detailed, educational explanation with examples and applications.";
        }
        else if (questionLower.Contains("physics") || questionLower.Contains("force") || questionLower.Contains("energy"))
        {
            return "You are an expert physics tutor. The student is asking about physics concepts. Provide clear explanations with examples and real-world applications.";
        }
        else if (questionLower.Contains("math") || questionLower.Contains("calculus") || questionLower.Contains("algebra"))
        {
            return "You are an expert mathematics tutor. The student is asking about mathematical concepts. Provide step-by-step explanations with examples.";
        }
        else if (questionLower.Contains("history") || questionLower.Contains("war") || questionLower.Contains("historical"))
        {
            return "You are an expert history tutor. The student is asking about historical events or concepts. Provide comprehensive context and analysis.";
        }

        return "You are a knowledgeable educational tutor. The student is asking about a topic not in your specialized knowledge base. Provide the best educational answer you can with clear explanations and encourage further learning.";
    }

    private int ExtractResultCount(string searchResultJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(searchResultJson);
            return doc.RootElement.TryGetProperty("result", out var result)
                ? result.GetArrayLength() : 0;
        }
        catch
        {
            return 0;
        }
    }

    private float[] GenerateDummyVector(int size)
    {
        var random = new Random();
        return Enumerable.Range(0, size).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();
    }

    [HttpPost("multilingual-ask")]
    public async Task<IActionResult> MultilingualAsk([FromBody] MultilingualQuestionRequest request)
    {
        try
        {
            var languageService = new LanguageService(_httpClient);

            // Step 1: Detect language if not provided
            var questionLanguage = request.Language;
            if (string.IsNullOrEmpty(questionLanguage) && request.AutoDetectLanguage)
            {
                var detection = await languageService.DetectLanguageAsync(request.Question);
                questionLanguage = detection.DetectedLanguage;
            }

            // Step 2: Translate question to English for RAG search
            var englishQuestion = request.Question;
            if (questionLanguage != "en")
            {
                englishQuestion = await languageService.TranslateTextAsync(request.Question, questionLanguage!, "en");
            }

            // Step 3: Perform RAG search with English question
            var queryEmbedding = await GenerateEmbedding(englishQuestion);
            var searchResults = await SearchQdrantContext(queryEmbedding);
            var context = ExtractEnhancedContext(searchResults, englishQuestion);

            // Step 4: Generate response in requested language
            var systemPrompt = GenerateMultilingualSystemPrompt(context, questionLanguage!, languageService.GetLanguageName(questionLanguage!));
            var response = await GenerateMultilingualResponse(request.Question, context, systemPrompt, questionLanguage!);

            // Step 5: Optional translation back to question language
            var finalResponse = response;
            if (request.TranslateResponse && questionLanguage != "en" && IsResponseInEnglish(response))
            {
                finalResponse = await languageService.TranslateTextAsync(response, "en", questionLanguage!);
            }

            return Ok(new MultilingualResponse
            {
                Question = request.Question,
                QuestionLanguage = questionLanguage!,
                EnglishTranslation = englishQuestion != request.Question ? englishQuestion : "",
                Answer = finalResponse,
                AnswerLanguage = questionLanguage!,
                TranslatedAnswer = request.TranslateResponse && finalResponse != response ? finalResponse : null,
                ContextSources = ExtractContextSources(searchResults),
                UsedFallback = string.IsNullOrEmpty(context) || context.Contains("No relevant context"),
                ProcessedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error in multilingual ask: {ex.Message}");
        }
    }

    [HttpGet("supported-languages")]
    public IActionResult GetSupportedLanguages()
    {
        var languageService = new LanguageService(_httpClient);
        var languages = languageService.GetSupportedLanguages();

        return Ok(new
        {
            supported_languages = languages,
            total_count = languages.Count,
            primary_languages = languages.Where(l => new[] { "en", "es", "fr", "hi", "de" }.Contains(l.Code)).ToList(),
            usage_examples = new
            {
                english = "What is machine learning?",
                spanish = "[translate:¿Qué es el aprendizaje automático?]",
                french = "[translate:Qu'est-ce que l'apprentissage automatique?]",
                hindi = "[translate:मशीन लर्निंग क्या है?]",
                german = "[translate:Was ist maschinelles Lernen?]"
            }
        });
    }

    [HttpPost("detect-language")]
    public async Task<IActionResult> DetectLanguage([FromBody] LanguageDetectionRequest request)
    {
        try
        {
            var languageService = new LanguageService(_httpClient);
            var result = await languageService.DetectLanguageAsync(request.Text);

            return Ok(new
            {
                text = request.Text,
                detected_language = result.DetectedLanguage,
                language_name = result.LanguageName,
                confidence = result.Confidence,
                is_supported = languageService.IsLanguageSupported(result.DetectedLanguage)
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error detecting language: {ex.Message}");
        }
    }

    [HttpGet("chat-sessions")]
    public async Task<IActionResult> GetChatSessions([FromServices] IChatService chatService)
    {
        try
        {
            var userId = User?.FindFirst("sub")?.Value ?? "anonymous-user";
            var sessions = await chatService.GetUserSessionsAsync(userId);

            return Ok(new
            {
                sessions = sessions.Select(s => new
                {
                    id = s.Id,
                    title = s.Title,
                    created_at = s.CreatedAt,
                    updated_at = s.UpdatedAt,
                    message_count = s.Messages?.Count ?? 0,
                    is_active = s.IsActive
                }),
                total = sessions.Count
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error getting chat sessions: {ex.Message}");
        }
    }

    [HttpGet("chat-sessions/{sessionId}/messages")]
    public async Task<IActionResult> GetSessionMessages(
        string sessionId,
        [FromServices] IChatService chatService)
    {
        try
        {
            var messages = await chatService.GetSessionMessagesAsync(sessionId);

            return Ok(new
            {
                session_id = sessionId,
                messages = messages.Select(m => new
                {
                    id = m.Id,
                    question = m.Question,
                    answer = m.Answer,
                    created_at = m.CreatedAt,
                    used_rag = m.UsedRAG,
                    search_score = m.SearchScore
                }),
                total = messages.Count
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error getting messages: {ex.Message}");
        }
    }


    // Helper Methods for Multilingual Support
    private string GenerateMultilingualSystemPrompt(string context, string languageCode, string languageName)
    {
        if (languageCode == "en")
        {
            return $@"You are an expert educational tutor. Answer questions clearly and educationally using the provided context.

Context: {context}";
        }

        return $@"You are an expert educational tutor. Answer the question in {languageName}. 
Provide clear, educational responses using the context provided. 
Keep your response in {languageName} throughout.

Context: {context}";
    }

    private async Task<string> GenerateMultilingualResponse(string question, string context, string systemPrompt, string languageCode)
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost:5000");
        _httpClient.DefaultRequestHeaders.Add("X-Title", "MyLearningPlatform");

        var chatBody = new
        {
            model = "meta-llama/llama-3.3-8b-instruct:free",
            messages = new object[]
            {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = question }
            }
        };

        var chatContent = new StringContent(JsonSerializer.Serialize(chatBody), Encoding.UTF8, "application/json");
        var chatResponse = await _httpClient.PostAsync("https://openrouter.ai/api/v1/chat/completions", chatContent);
        var chatResult = await chatResponse.Content.ReadAsStringAsync();

        return ExtractAIResponseText(chatResult);
    }

    private bool IsResponseInEnglish(string response)
    {
        // Simple heuristic to check if response is in English
        var englishWords = new[] { "the", "and", "is", "are", "this", "that", "with", "for", "can", "will" };
        return englishWords.Any(word => response.ToLower().Contains(" " + word + " "));
    }

    private List<string> ExtractContextSources(string searchResults)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(searchResults);
            var sources = new List<string>();

            if (document.RootElement.TryGetProperty("result", out var resultElement))
            {
                foreach (var item in resultElement.EnumerateArray())
                {
                    if (item.TryGetProperty("payload", out var payload))
                    {
                        var title = payload.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : "";
                        var subject = payload.TryGetProperty("subject", out var subjectProp) ? subjectProp.GetString() : "";

                        if (!string.IsNullOrEmpty(title))
                        {
                            sources.Add($"{subject}: {title}");
                        }
                    }
                }
            }

            return sources;
        }
        catch
        {
            return new List<string>();
        }
    }

    public class LanguageDetectionRequest
    {
        public string Text { get; set; } = string.Empty;
    }
    private async Task<string> SearchQdrantContext(float[] queryEmbedding)
    {
        try
        {
            using var searchHttpClient = new HttpClient();
            var searchBody = new
            {
                vector = queryEmbedding,
                limit = 5,
                with_payload = true,
                score_threshold = 0.2
            };

            var searchContent = new StringContent(JsonSerializer.Serialize(searchBody), Encoding.UTF8, "application/json");
            var searchResponse = await searchHttpClient.PostAsync(
                "http://localhost:6333/collections/learning_content/points/search", searchContent);

            if (searchResponse.IsSuccessStatusCode)
            {
                return await searchResponse.Content.ReadAsStringAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error searching Qdrant: {ex.Message}");
        }

        return ""; // Return empty string if search fails
    }

    private string ExtractAIResponseText(string aiResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(aiResponse);
            return doc.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting AI response: {ex.Message}");
            return "I'm here to help with your educational questions!";
        }
    }
}
