using System.Text;
using AI_driven_teaching_platform.Models;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AI_driven_teaching_platform.Services
{
    public class DocumentService
    {
        private readonly HttpClient _httpClient;

        public DocumentService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<DocumentProcessResult> ProcessDocument(
            IFormFile file,
            string userId = "anonymous",
            string? documentId = null,
            IDatabaseDocumentService? dbDocumentService = null)  // ✅ Add this
        {
            try
            {
                // ✅ Use provided documentId or generate new one
                var docId = documentId ?? Guid.NewGuid().ToString();

                Console.WriteLine($"[ProcessDocument] Starting processing for {file.FileName} with ID: {docId}");

                var content = await ReadFileContent(file);
                Console.WriteLine($"[ProcessDocument] Content length: {content.Length}");

                var chunks = SplitIntoChunks(content, 1000);
                Console.WriteLine($"[ProcessDocument] Created {chunks.Count} chunks");

                var qdrantClient = new QdrantClient("localhost", 6334);
                Console.WriteLine($"[ProcessDocument] Connected to Qdrant");

                var points = new List<PointStruct>();
                int successfulChunks = 0;

                for (int i = 0; i < chunks.Count; i++)
                {
                    try
                    {
                        var embedding = await GenerateEmbedding(chunks[i]);
                        var pointId = (ulong)(docId.GetHashCode() + i);

                        // ✅ Save to Qdrant
                        var point = new PointStruct { Id = pointId, Vectors = embedding };
                        point.Payload.Add("title", new Value { StringValue = file.FileName });
                        point.Payload.Add("content", new Value { StringValue = chunks[i] });
                        point.Payload.Add("document_id", new Value { StringValue = docId });
                        point.Payload.Add("user_id", new Value { StringValue = userId });
                        point.Payload.Add("chunk_index", new Value { StringValue = i.ToString() });
                        point.Payload.Add("uploaded_at", new Value { StringValue = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") });

                        points.Add(point);

                        // ✅ ALSO save to PostgreSQL if service is provided
                        if (dbDocumentService != null)
                        {
                            var dbChunk = new DocumentChunk
                            {
                                Id = Guid.NewGuid().ToString(),
                                DocumentId = docId,
                                ChunkIndex = i,
                                Content = chunks[i],
                                StartPosition = i * 1000,  // Approximate position
                                EndPosition = (i * 1000) + chunks[i].Length,
                                QdrantPointId = pointId,
                                CreatedAt = DateTime.UtcNow
                            };

                            await dbDocumentService.SaveDocumentChunkAsync(dbChunk);
                            Console.WriteLine($"[ProcessDocument] Saved chunk {i} to database");
                        }

                        successfulChunks++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ProcessDocument] ERROR processing chunk {i}: {ex.Message}");
                    }
                }

                Console.WriteLine($"[ProcessDocument] Upserting {points.Count} points to Qdrant");
                await qdrantClient.UpsertAsync("learning_content", points);

                Console.WriteLine($"[ProcessDocument] Successfully processed {file.FileName}: {successfulChunks}/{chunks.Count} chunks");

                return new DocumentProcessResult
                {
                    DocumentId = docId,
                    FileName = file.FileName,
                    FileSize = file.Length,
                    ContentPreview = content.Length > 300 ? content[..300] + "..." : content,
                    ChunkCount = successfulChunks
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ProcessDocument] ERROR: {ex.Message}");
                Console.WriteLine($"[ProcessDocument] Stack: {ex.StackTrace}");
                throw;
            }
        }

        public async Task<List<DocumentInfo>> GetUserDocuments(string? userId = null)
        {
            using var httpClient = new HttpClient();
            var scrollBody = userId != null ? new
            {
                filter = new
                {
                    must = new[]
                    {
                        new { key = "document_type", match = new { value = "user_uploaded" } },
                        new { key = "user_id", match = new { value = userId } }
                    }
                }
            } : new
            {
                filter = new
                {
                    must = new[]
                    {
                        new { key = "document_type", match = new { value = "user_uploaded" } }
                    }
                }
            };

            var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(scrollBody), Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync("http://localhost:6333/collections/learning_content/points/scroll?limit=1000", content);
            var resultJson = await response.Content.ReadAsStringAsync();

            return ParseDocumentList(resultJson);
        }

        public async Task<DocumentInfo?> GetDocumentInfo(string documentId)
        {
            var documents = await GetUserDocuments();
            return documents.FirstOrDefault(d => d.DocumentId == documentId);
        }

        public async Task<bool> DeleteDocument(string documentId)
        {
            try
            {
                using var httpClient = new HttpClient();
                var deleteBody = new
                {
                    filter = new
                    {
                        must = new[]
                        {
                            new { key = "document_id", match = new { value = documentId } }
                        }
                    }
                };

                var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(deleteBody), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync("http://localhost:6333/collections/learning_content/points/delete", content);

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> ReadFileContent(IFormFile file)
        {
            var extension = Path.GetExtension(file.FileName).ToLower();

            switch (extension)
            {
                case ".txt":
                    using (var reader = new StreamReader(file.OpenReadStream()))
                    {
                        return await reader.ReadToEndAsync();
                    }

                case ".pdf":
                    // For now, return placeholder. Add PDF processing library later
                    return "PDF processing not implemented yet. Please upload TXT files.";

                case ".docx":
                    // For now, return placeholder. Add Word processing library later
                    return "DOCX processing not implemented yet. Please upload TXT files.";

                default:
                    throw new ArgumentException($"Unsupported file type: {extension}");
            }
        }

        private List<string> SplitIntoChunks(string text, int maxChunkSize)
        {
            var chunks = new List<string>();
            var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            var currentChunk = new StringBuilder();

            foreach (var sentence in sentences)
            {
                if (currentChunk.Length + sentence.Length > maxChunkSize && currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }

                currentChunk.Append(sentence + ". ");
            }

            if (currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
            }

            return chunks.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        }

        private async Task<float[]> GenerateEmbedding(string text)
        {
            try
            {
                using var client = new HttpClient();
                var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

                if (string.IsNullOrEmpty(openaiKey))
                {
                    var rng = new Random();
                    return Enumerable.Range(0, 1536)
                                     .Select(_ => (float)(rng.NextDouble() * 2 - 1))
                                     .ToArray();
                }


                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", openaiKey);

                var body = new
                {
                    model = "text-embedding-3-small",
                    input = text.Length > 8192 ? text[..8192] : text
                };

                var content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.openai.com/v1/embeddings", content);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(result);
                    return doc.RootElement.GetProperty("data")[0]
                        .GetProperty("embedding").EnumerateArray()
                        .Select(x => (float)x.GetDouble()).ToArray();
                }

                // Fallback to dummy vector
                var random = new Random();
                return Enumerable.Range(0, 1536).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();
            }
            catch
            {
                var random = new Random();
                return Enumerable.Range(0, 1536).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();
            }
        }

        private List<DocumentInfo> ParseDocumentList(string jsonResult)
        {
            var documents = new List<DocumentInfo>();
            var processedDocIds = new HashSet<string>();

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(jsonResult);

                if (doc.RootElement.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("points", out var points))
                {
                    foreach (var point in points.EnumerateArray())
                    {
                        if (point.TryGetProperty("payload", out var payload))
                        {
                            var docId = payload.TryGetProperty("document_id", out var docIdProp) ? docIdProp.GetString() : "";

                            if (!string.IsNullOrEmpty(docId) && !processedDocIds.Contains(docId))
                            {
                                processedDocIds.Add(docId);

                                documents.Add(new DocumentInfo
                                {
                                    DocumentId = docId,
                                    FileName = payload.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "",
                                    FileType = payload.TryGetProperty("file_type", out var typeProp) ? typeProp.GetString() ?? "" : "",
                                    UploadedAt = payload.TryGetProperty("uploaded_at", out var dateProp) ? dateProp.GetString() ?? "" : "",
                                    FileSize = 0, // Would need to be calculated from chunks
                                    ChunkCount = 1 // Would need to be counted
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing document list: {ex.Message}");
            }

            return documents;
        }
    }
}
