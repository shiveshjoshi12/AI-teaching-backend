using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AI_driven_teaching_platform.Models;
using AI_driven_teaching_platform.Services;
using AI_driven_teaching_platform.Data;
using Microsoft.EntityFrameworkCore;
using Qdrant.Client.Grpc;
using Qdrant.Client;

namespace AI_driven_teaching_platform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly HttpClient _httpClient;
        private readonly OpenRouterSettings _settings;
        private readonly DocumentService _documentService;
        private readonly IDatabaseDocumentService _dbDocumentService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DocumentController> _logger;
        private readonly IConfiguration _configuration;

        public DocumentController(
            IHttpClientFactory httpClientFactory,
            IOptions<OpenRouterSettings> options,
            DocumentService documentService,
            IDatabaseDocumentService dbDocumentService,
            ApplicationDbContext context,
            ILogger<DocumentController> logger,
            IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _httpClient = httpClientFactory.CreateClient();
            _settings = options.Value;
            _documentService = documentService;
            _dbDocumentService = dbDocumentService;
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadDocument([FromForm] DocumentUploadRequest request)
        {
            try
            {
                if (request.File == null || request.File.Length == 0)
                    return BadRequest(new { error = "No file uploaded" });

                var allowedExtensions = new[] { ".txt", ".pdf", ".docx" };
                var fileExtension = Path.GetExtension(request.File.FileName).ToLower();

                if (!allowedExtensions.Contains(fileExtension))
                    return BadRequest(new { error = $"Unsupported file type" });

                var authenticatedUserId = GetAuthenticatedUserId();

                if (string.IsNullOrEmpty(authenticatedUserId))
                {
                    _logger.LogWarning("⚠️ Unauthorized upload attempt");
                    return Unauthorized(new { error = "Authentication required. Please log in." });
                }

                _logger.LogInformation($"✅ User {authenticatedUserId} uploading: {request.File.FileName}");

                var dbDocument = await _dbDocumentService.CreateDocumentAsync(
                    title: request.Title ?? Path.GetFileNameWithoutExtension(request.File.FileName),
                    fileName: request.File.FileName,
                    contentType: request.File.ContentType,
                    fileSize: request.File.Length,
                    uploadedBy: authenticatedUserId,
                    subject: request.Subject ?? "General",
                    grade: request.Grade ?? "All"
                );

                try
                {
                    // ✅ CRITICAL: Pass _dbDocumentService here!
                    var result = await _documentService.ProcessDocument(
                        request.File,
                        authenticatedUserId,
                        dbDocument.Id,
                        _dbDocumentService  // ✅ THIS IS THE KEY!
                    );

                    await _dbDocumentService.UpdateProcessingStatusAsync(dbDocument.Id, "completed");

                    return Ok(new
                    {
                        document_id = dbDocument.Id,
                        title = dbDocument.Title,
                        filename = dbDocument.FileName,
                        file_size = dbDocument.FileSize,
                        uploaded_at = dbDocument.UploadedAt,
                        processing_status = "completed",
                        chunk_count = result.ChunkCount
                    });
                }
                catch (Exception processEx)
                {
                    _logger.LogError(processEx, "Processing failed");
                    await _dbDocumentService.UpdateProcessingStatusAsync(dbDocument.Id, "failed", processEx.Message);

                    return Ok(new
                    {
                        document_id = dbDocument.Id,
                        title = dbDocument.Title,
                        filename = dbDocument.FileName,
                        file_size = dbDocument.FileSize,
                        uploaded_at = dbDocument.UploadedAt,
                        processing_status = "failed",
                        error = processEx.Message
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Upload error");
                return StatusCode(500, new { error = $"Error uploading: {ex.Message}" });
            }
        }



        [HttpGet("list")]
        public async Task<IActionResult> ListDocuments()
        {
            try
            {
                // ✅ REQUIRE authentication - no fallback
                var authenticatedUserId = GetAuthenticatedUserId();

                if (string.IsNullOrEmpty(authenticatedUserId))
                {
                    _logger.LogWarning("⚠️ Unauthorized access attempt to list documents");
                    return Unauthorized(new { error = "Authentication required. Please log in." });
                }

                _logger.LogInformation($"✅ User {authenticatedUserId} listing their documents");

                // ✅ This already filters by userId
                var documents = await _dbDocumentService.GetUserDocumentsAsync(authenticatedUserId);

                return Ok(new
                {
                    documents = documents.Select(d => new
                    {
                        document_id = d.Id,
                        title = d.Title,
                        filename = d.FileName,
                        file_size = d.FileSize,
                        uploaded_at = d.UploadedAt,
                        processing_status = d.ProcessingStatus,
                        chunk_count = d.TotalChunks,
                        subject = d.Subject,
                        grade = d.Grade
                    }),
                    total_count = documents.Count,
                    total_size = documents.Sum(d => d.FileSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "List error");
                return StatusCode(500, new { error = $"Error listing documents: {ex.Message}" });
            }
        }


        [HttpPost("ask/{documentId}")]
        public async Task<IActionResult> AskDocument(string documentId, [FromBody] DocumentQuestionRequest request)
        {
            try
            {
                _logger.LogInformation($"=== ASK DOCUMENT === Doc ID: {documentId}, Question: {request.Question}");

                // ✅ Authenticate user (using YOUR method)
                var authenticatedUserId = GetAuthenticatedUserId();

                if (string.IsNullOrEmpty(authenticatedUserId))
                {
                    _logger.LogWarning("⚠️ Unauthorized ask attempt");
                    return Unauthorized(new { error = "Authentication required" });
                }

                // ✅ Verify document exists and user owns it
                var document = await _dbDocumentService.GetDocumentByIdAsync(documentId);
                if (document == null)
                {
                    _logger.LogWarning($"Document not found: {documentId}");
                    return NotFound(new { error = "Document not found" });
                }

                if (document.UploadedBy != authenticatedUserId)
                {
                    _logger.LogWarning($"⚠️ User {authenticatedUserId} tried to access document owned by {document.UploadedBy}");
                    return Forbid();
                }

                _logger.LogInformation($"✅ Document found: {document.Title}");

                // ✅ Generate embedding for question (using IMPROVED version below)
                var questionEmbedding = await GenerateEmbedding(request.Question);
                _logger.LogInformation($"✅ Generated embedding for question");

                // ✅ Search Qdrant for relevant chunks
                var qdrantClient = new QdrantClient("localhost", 6334);

                var filter = new Filter
                {
                    Must = {
                new Condition
                {
                    Field = new FieldCondition
                    {
                        Key = "document_id",
                        Match = new Match { Keyword = documentId }
                    }
                }
            }
                };

                var searchResult = await qdrantClient.SearchAsync(
                    collectionName: "learning_content",
                    vector: questionEmbedding,
                    filter: filter,
                    limit: 5
                );

                _logger.LogInformation($"✅ Found {searchResult.Count} relevant chunks");

                if (searchResult == null || searchResult.Count == 0)
                {
                    return Ok(new
                    {
                        answer = "I couldn't find relevant information in this document to answer your question. Try rephrasing your question or ask something else about the document.",
                        context_used = new string[] { },
                        confidence = 0.0,
                        chunks_found = 0
                    });
                }

                // ✅ Extract relevant content
                var contexts = searchResult
                    .Select(r => r.Payload["content"].StringValue)
                    .Take(3)
                    .ToList();

                var contextText = string.Join("\n\n", contexts);
                var avgConfidence = searchResult.Average(r => r.Score);

                _logger.LogInformation($"✅ Using {contexts.Count} chunks with avg confidence {avgConfidence:F2}");

                // ✅ Generate AI answer (using YOUR GenerateAnswerWithOpenRouter method)
                var aiAnswer = await GenerateAnswerWithOpenRouter(
                    question: request.Question,
                    context: contextText,
                    documentTitle: document.Title
                );

                _logger.LogInformation($"✅ Generated AI answer");

                return Ok(new
                {
                    answer = aiAnswer,
                    context_used = contexts.Select(c => c.Length > 200 ? c.Substring(0, 200) + "..." : c).ToList(),
                    confidence = avgConfidence,
                    chunks_found = searchResult.Count,
                    document_title = document.Title
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Error asking document {documentId}: {ex.Message}");
                return StatusCode(500, new { error = $"Error: {ex.Message}" });
            }
        }



        [HttpDelete("{documentId}")]
        public async Task<IActionResult> DeleteDocument(string documentId)
        {
            try
            {
                // ✅ REQUIRE authentication
                var authenticatedUserId = GetAuthenticatedUserId();

                if (string.IsNullOrEmpty(authenticatedUserId))
                {
                    _logger.LogWarning("⚠️ Unauthorized delete attempt");
                    return Unauthorized(new { error = "Authentication required" });
                }

                var document = await _dbDocumentService.GetDocumentByIdAsync(documentId);
                if (document == null)
                {
                    return NotFound(new { error = "Document not found" });
                }

                // ✅ VERIFY: User can only delete their OWN documents
                if (document.UploadedBy != authenticatedUserId)
                {
                    _logger.LogWarning($"⚠️ User {authenticatedUserId} tried to delete document owned by {document.UploadedBy}");
                    return Forbid(); // 403 Forbidden
                }

                await _dbDocumentService.DeleteDocumentAsync(documentId);

                _logger.LogInformation($"✅ Document deleted: {documentId} by {authenticatedUserId}");

                return Ok(new
                {
                    message = "Document deleted successfully",
                    document_id = documentId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Delete error");
                return StatusCode(500, new { error = $"Error deleting: {ex.Message}" });
            }
        }


        // Helper Methods
        private string? GetAuthenticatedUserId()
        {
            try
            {
                var authHeader = Request.Headers["Authorization"].FirstOrDefault();

                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    _logger.LogWarning("⚠️ No Bearer token found");
                    return null;
                }

                var token = authHeader.Substring("Bearer ".Length).Trim();

                // Decode JWT to get claims
                var parts = token.Split('.');
                if (parts.Length != 3)
                {
                    _logger.LogWarning("⚠️ Invalid JWT format");
                    return null;
                }

                var payload = parts[1];
                // Add padding if needed for Base64 decoding
                payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

                var jsonBytes = Convert.FromBase64String(payload);
                var json = System.Text.Encoding.UTF8.GetString(jsonBytes);

                _logger.LogInformation($"📋 Token payload: {json}");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // ✅ Your AuthController puts user ID in "sub" claim
                if (root.TryGetProperty("sub", out var subClaim))
                {
                    var userId = subClaim.GetString();
                    _logger.LogInformation($"✅ Found user ID in 'sub' claim: {userId}");
                    return userId;
                }

                // Also try "nameid" as fallback
                if (root.TryGetProperty("nameid", out var nameIdClaim))
                {
                    var userId = nameIdClaim.GetString();
                    _logger.LogInformation($"✅ Found user ID in 'nameid' claim: {userId}");
                    return userId;
                }

                _logger.LogWarning("⚠️ No user ID found in token claims");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error extracting user ID from token");
                return null;
            }
        }

        private async Task<string> GetOrCreateTestUserAsync()
        {
            var testEmail = "test@example.com";
            var testUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == testEmail);

            if (testUser == null)
            {
                testUser = new User
                {
                    Id = Guid.NewGuid().ToString(),
                    GoogleId = "test-google-id",
                    Email = testEmail,
                    Name = "Test User",
                    Picture = "https://via.placeholder.com/150",
                    Role = "Student",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    LastLoginAt = DateTime.UtcNow
                };

                _context.Users.Add(testUser);
                await _context.SaveChangesAsync();
            }

            return testUser.Id;
        }

        private async Task<float[]> GenerateEmbedding(string text)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();

                var requestBody = new
                {
                    model = "text-embedding-3-small",
                    input = text
                };

                var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/embeddings");

                // Check if API key exists
                var apiKey = _configuration["OpenAI:ApiKey"];
                if (string.IsNullOrEmpty(apiKey))
                {
                    _logger.LogWarning("⚠️ OpenAI API key not configured, using fallback embedding");
                    // Fallback to deterministic embedding (your old method)
                    var random = new Random(text.GetHashCode());
                    return Enumerable.Range(0, 1536).Select(_ => (float)random.NextDouble()).ToArray();
                }

                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                request.Content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await client.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"⚠️ OpenAI embedding failed: {response.StatusCode}, using fallback");
                    // Fallback to deterministic embedding
                    var random = new Random(text.GetHashCode());
                    return Enumerable.Range(0, 1536).Select(_ => (float)random.NextDouble()).ToArray();
                }

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<JsonElement>(content);

                var embedding = result
                    .GetProperty("data")[0]
                    .GetProperty("embedding")
                    .EnumerateArray()
                    .Select(e => (float)e.GetDouble())
                    .ToArray();

                _logger.LogInformation($"✅ Generated real OpenAI embedding");
                return embedding;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating embedding, using fallback");
                // Fallback to deterministic embedding
                var random = new Random(text.GetHashCode());
                return Enumerable.Range(0, 1536).Select(_ => (float)random.NextDouble()).ToArray();
            }
        }
        private string ExtractContextFromSearch(string searchResultJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(searchResultJson);
                if (doc.RootElement.TryGetProperty("result", out var result))
                {
                    var contexts = new List<string>();
                    foreach (var item in result.EnumerateArray())
                    {
                        if (item.TryGetProperty("payload", out var payload) &&
                            payload.TryGetProperty("content", out var content))
                        {
                            contexts.Add(content.GetString() ?? "");
                        }
                    }
                    return contexts.Any() ? string.Join("\n\n", contexts) : "No relevant context found.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting context");
            }

            return "No relevant context found.";
        }

        private async Task<string> GenerateAnswerWithOpenRouter(string question, string context, string documentTitle)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Clear();
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
                client.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost:5173");
                client.DefaultRequestHeaders.Add("X-Title", "AI Teaching Platform");

                var systemPrompt = $@"You are an expert tutor helping students understand the document '{documentTitle}'. 
Use the following context from the document to answer the question accurately and clearly.

Context from document:
{context}

Answer the question based on this context. If the context doesn't contain enough information, say so.";

                var chatBody = new
                {
                    model = "meta-llama/llama-3.3-8b-instruct:free",
                    messages = new object[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user", content = question }
                    }
                };

                var chatContent = new StringContent(
                    JsonSerializer.Serialize(chatBody),
                    Encoding.UTF8,
                    "application/json");

                var chatResponse = await client.PostAsync(
                    "https://openrouter.ai/api/v1/chat/completions",
                    chatContent);

                var chatResult = await chatResponse.Content.ReadAsStringAsync();

                using var responseDoc = JsonDocument.Parse(chatResult);
                if (responseDoc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0)
                {
                    var firstChoice = choices[0];
                    if (firstChoice.TryGetProperty("message", out var message) &&
                        message.TryGetProperty("content", out var content))
                    {
                        return content.GetString() ?? "I couldn't generate a response.";
                    }
                }

                return "I couldn't generate a response.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating answer with OpenRouter");
                return "Sorry, I encountered an error generating the answer.";
            }
        }
    }
}
