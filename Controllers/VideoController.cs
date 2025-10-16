using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AI_driven_teaching_platform.Models;
using AI_driven_teaching_platform.Services;

[ApiController]
[Route("api/[controller]")]
public class VideoController : ControllerBase
{
    private readonly HttpClient _httpClient;
    private readonly OpenRouterSettings _openRouterSettings;
    private readonly HeyGenService _heyGenService;

    public VideoController(
        IHttpClientFactory httpClientFactory,
        IOptions<OpenRouterSettings> openRouterOptions,
        HeyGenService heyGenService)
    {
        _httpClient = httpClientFactory.CreateClient();
        _openRouterSettings = openRouterOptions.Value;
        _heyGenService = heyGenService;
    }

    /// <summary>
    /// Test HeyGen API connectivity and check remaining quota
    /// </summary>
    [HttpGet("test-connection")]
    public async Task<IActionResult> TestConnection()
    {
        try
        {
            var isConnected = await _heyGenService.TestConnectionAsync();

            if (isConnected)
            {
                // Get quota information
                var apiKey = Environment.GetEnvironmentVariable("HEYGEN_API_KEY") ??
                             _heyGenService.GetApiKey();

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var response = await _httpClient.GetAsync("https://api.heygen.com/v2/user/remaining_quota");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return Ok(new
                    {
                        heygen_connected = true,
                        quota_info = JsonSerializer.Deserialize<object>(content),
                        message = "HeyGen API is ready for video generation!",
                        timestamp = DateTime.UtcNow
                    });
                }
            }

            return Ok(new
            {
                heygen_connected = false,
                message = "HeyGen API connection failed. Check your API key.",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                heygen_connected = false,
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Create a video from text using HeyGen
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> CreateVideo([FromBody] CreateVideoRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Text))
                return BadRequest("Text content is required for video generation");

            // Set working defaults
            request.AvatarId = request.AvatarId ?? "Tyler-insuit-20220721";
            request.Title = request.Title ?? "AI Teaching Video";

            var result = await _heyGenService.CreateVideoAsync(request);

            return Ok(new
            {
                message = "Video generation started successfully!",
                video_id = result.VideoId,
                status = result.Status,
                avatar_used = result.AvatarUsed,
                text_length = request.Text.Length,
                estimated_completion_time = "2-5 minutes",
                check_status_url = $"/api/video/status/{result.VideoId}",
                created_at = result.CreatedAt
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Video generation failed",
                details = ex.Message,
                recommendation = "Check HeyGen API key and account credits"
            });
        }
    }

    /// RAG + Video Generation - The main feature of your platform
    [HttpPost("ask-with-video")]
    public async Task<IActionResult> AskWithVideo([FromBody] VideoQuestionRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Question))
                return BadRequest("Question is required");

            // Step 1: Get AI response using RAG pipeline
            var aiResponse = await GetSmartAIResponse(request.Question);

            if (string.IsNullOrEmpty(aiResponse))
                return StatusCode(500, "Failed to generate AI response");

            // Step 2: Create video with AI response
            var videoRequest = new CreateVideoRequest
            {
                Text = aiResponse,
                AvatarId = request.AvatarId ?? "Tyler-insuit-20220721",
                Voice = request.Voice,
                Title = $"AI Answer: {TruncateString(request.Question, 50)}"
            };

            var videoResult = await _heyGenService.CreateVideoAsync(videoRequest);

            return Ok(new
            {
                question = request.Question,
                ai_text_response = aiResponse,
                video_generation = new
                {
                    video_id = videoResult.VideoId,
                    status = videoResult.Status,
                    avatar_used = videoResult.AvatarUsed,
                    estimated_completion = DateTime.UtcNow.AddMinutes(3)
                },
                check_video_status_url = $"/api/video/status/{videoResult.VideoId}",
                pipeline_used = "RAG + HeyGen Video Generation",
                created_at = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "RAG + Video generation failed",
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Check video generation status and get download URL when ready
    /// </summary>
    [HttpGet("status/{videoId}")]
    public async Task<IActionResult> GetVideoStatus(string videoId)
    {
        try
        {
            var status = await _heyGenService.GetVideoStatusAsync(videoId);

            return Ok(new
            {
                video_id = videoId,
                status = status.Status,
                progress = status.Progress,
                video_url = status.VideoUrl,
                thumbnail_url = status.ThumbnailUrl,
                error_message = status.ErrorMessage,
                is_ready = status.Status?.ToLower() == "completed",
                is_failed = status.Status?.ToLower() == "failed" || status.Status?.ToLower() == "error",
                checked_at = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Failed to check video status",
                details = ex.Message,
                video_id = videoId
            });
        }
    }

    /// <summary>
    /// Get available avatars for video generation
    /// </summary>
    [HttpGet("avatars")]
    public async Task<IActionResult> GetAvailableAvatars()
    {
        try
        {
            var avatars = await _heyGenService.GetAvailableAvatarsAsync();

            return Ok(new
            {
                avatars = avatars,
                total_count = avatars.Count,
                recommended_avatar = "Tyler-insuit-20220721",
                message = "Available avatars for video generation"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Failed to get avatars",
                details = ex.Message
            });
        }
    }

    /// <summary>
    /// Create multiple videos in batch
    /// </summary>
    [HttpPost("batch-create")]
    public async Task<IActionResult> CreateBatchVideos([FromBody] BatchVideoRequest request)
    {
        try
        {
            if (request.Videos == null || !request.Videos.Any())
                return BadRequest("At least one video content is required");

            var results = new List<object>();
            var successCount = 0;

            foreach (var video in request.Videos)
            {
                try
                {
                    var createRequest = new CreateVideoRequest
                    {
                        Text = video.Answer,
                        AvatarId = request.AvatarId ?? "Tyler-insuit-20220721",
                        Voice = request.Voice,
                        Title = video.Title ?? $"Video for: {TruncateString(video.Question, 30)}"
                    };

                    var result = await _heyGenService.CreateVideoAsync(createRequest);

                    results.Add(new
                    {
                        question = video.Question,
                        video_id = result.VideoId,
                        status = result.Status,
                        success = true
                    });

                    successCount++;
                }
                catch (Exception ex)
                {
                    results.Add(new
                    {
                        question = video.Question,
                        error = ex.Message,
                        success = false
                    });
                }

                // Rate limiting delay
                await Task.Delay(2000);
            }

            return Ok(new
            {
                message = "Batch video generation completed",
                total_requested = request.Videos.Count,
                successful_videos = successCount,
                failed_videos = request.Videos.Count - successCount,
                results = results,
                processing_time = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Batch video generation failed",
                details = ex.Message
            });
        }
    }

    // Private Helper Methods
    private async Task<string> GetSmartAIResponse(string question)
    {
        try
        {
            // Use existing RAG pipeline logic
            var queryEmbedding = await GenerateEmbedding(question);
            var context = await SearchQdrantContext(queryEmbedding);

            return await GenerateAIResponse(question, context);
        }
        catch
        {
            // Fallback to simple AI response
            return await GenerateSimpleAIResponse(question);
        }
    }

    private async Task<float[]> GenerateEmbedding(string text)
    {
        try
        {
            using var client = new HttpClient();
            var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");

            if (string.IsNullOrEmpty(openaiKey))
                return GenerateDummyVector(1536);

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", openaiKey);

            var body = new { model = "text-embedding-3-small", input = text };
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

    private async Task<string> SearchQdrantContext(float[] queryEmbedding)
    {
        try
        {
            using var searchHttpClient = new HttpClient();
            var searchBody = new
            {
                vector = queryEmbedding,
                limit = 3,
                with_payload = true,
                score_threshold = 0.3
            };

            var searchContent = new StringContent(JsonSerializer.Serialize(searchBody), Encoding.UTF8, "application/json");
            var searchResponse = await searchHttpClient.PostAsync(
                "http://localhost:6333/collections/learning_content/points/search", searchContent);

            if (searchResponse.IsSuccessStatusCode)
            {
                var searchResultJson = await searchResponse.Content.ReadAsStringAsync();
                return ExtractContext(searchResultJson);
            }
        }
        catch { }

        return "General educational knowledge available.";
    }

    private async Task<string> GenerateAIResponse(string question, string context)
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _openRouterSettings.ApiKey);
        _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "http://localhost:5000");
        _httpClient.DefaultRequestHeaders.Add("X-Title", "MyLearningPlatform");

        var systemPrompt = $@"You are an expert educational tutor creating content for video generation. 
Provide clear, engaging explanations suitable for video format (60-90 seconds of speech).
Use simple language, short sentences, and educational tone. Keep responses concise but informative.

Context: {context}";

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

    private async Task<string> GenerateSimpleAIResponse(string question)
    {
        return await GenerateAIResponse(question, "General educational knowledge.");
    }

    private string ExtractContext(string searchResultJson)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(searchResultJson);

            if (document.RootElement.TryGetProperty("result", out var resultElement))
            {
                var contextParts = new List<string>();

                foreach (var item in resultElement.EnumerateArray())
                {
                    if (item.TryGetProperty("payload", out var payload))
                    {
                        var content = payload.TryGetProperty("content", out var contentProp) ?
                            contentProp.GetString() : "";

                        if (!string.IsNullOrEmpty(content))
                        {
                            contextParts.Add(content);
                        }
                    }
                }

                return contextParts.Count > 0 ? string.Join(" ", contextParts) : "No specific context found.";
            }

            return "No context available.";
        }
        catch
        {
            return "Error retrieving context.";
        }
    }

    private string ExtractAIResponseText(string aiResponse)
    {
        try
        {
            using var doc = JsonDocument.Parse(aiResponse);
            return doc.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch
        {
            return "I'm here to help with your educational questions!";
        }
    }

    private float[] GenerateDummyVector(int size)
    {
        var random = new Random();
        return Enumerable.Range(0, size).Select(_ => (float)(random.NextDouble() * 2 - 1)).ToArray();
    }

    private string TruncateString(string input, int maxLength)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return input.Length > maxLength ? input[..maxLength] + "..." : input;
    }

    [HttpPost("create-working")]
    public async Task<IActionResult> CreateWorkingVideo([FromBody] CreateVideoRequest request)
    {
        try
        {
            var apiKey = Environment.GetEnvironmentVariable("HEYGEN_API_KEY") ??
                         _heyGenService.GetApiKey();

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            // Common working HeyGen voice IDs
            var workingVoiceIds = new[]
            {
            "1bd001e7e50f421d891986aad5158bc8",  // Common English voice
            "40532fee80a741ae8fe05213e276041b",  // Alternative English voice
            "e73622ed07c944ba8d7d5de090a6539b",  // Another option
            "694f8b8ef4b548859ea2a9b3c3ed7e8b"   // Backup option
        };

            var results = new List<object>();

            // Try each voice ID until one works
            foreach (var voiceId in workingVoiceIds)
            {
                try
                {
                    var videoPayload = new
                    {
                        video_inputs = new[]
                        {
                        new
                        {
                            character = new
                            {
                                type = "avatar",
                                avatar_id = "Tyler-insuit-20220721"
                            },
                            voice = new
                            {
                                type = "text",
                                input_text = request.Text ?? "Hello! Welcome to our AI teaching platform.",
                                voice_id = voiceId  // Try this voice ID
                            }
                        }
                    }
                    };

                    var jsonContent = JsonSerializer.Serialize(videoPayload);
                    var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PostAsync("https://api.heygen.com/v2/video/generate", content);
                    var responseString = await response.Content.ReadAsStringAsync();

                    results.Add(new
                    {
                        voice_id = voiceId,
                        success = response.IsSuccessStatusCode,
                        status_code = response.StatusCode,
                        response = responseString
                    });

                    if (response.IsSuccessStatusCode)
                    {
                        var result = JsonSerializer.Deserialize<JsonElement>(responseString);
                        string videoId = "";

                        if (result.TryGetProperty("data", out var data) &&
                            data.TryGetProperty("video_id", out var vidId))
                        {
                            videoId = vidId.GetString() ?? "";
                        }

                        return Ok(new
                        {
                            message = "SUCCESS! Video generation started!",
                            video_id = videoId,
                            working_avatar_id = "Tyler-insuit-20220721",
                            working_voice_id = voiceId,
                            text_content = request.Text,
                            check_status_url = $"/api/video/status/{videoId}",
                            all_attempts = results
                        });
                    }

                    await Task.Delay(500); // Small delay between attempts
                }
                catch (Exception ex)
                {
                    results.Add(new
                    {
                        voice_id = voiceId,
                        error = ex.Message,
                        success = false
                    });
                }
            }

            return Ok(new
            {
                message = "All voice IDs attempted",
                attempts = results,
                recommendation = "Check HeyGen documentation for valid voice IDs for your account"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "Video generation error",
                details = ex.Message
            });
        }
    }
    [HttpGet("get-voices")]
    public async Task<IActionResult> GetAvailableVoices()
    {
        try
        {
            var apiKey = Environment.GetEnvironmentVariable("HEYGEN_API_KEY") ??
                         _heyGenService.GetApiKey();

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

            var response = await _httpClient.GetAsync("https://api.heygen.com/v2/voices");
            var responseString = await response.Content.ReadAsStringAsync();

            return Ok(new
            {
                success = response.IsSuccessStatusCode,
                status_code = response.StatusCode,
                voices_response = response.IsSuccessStatusCode ?
                    JsonSerializer.Deserialize<object>(responseString) : null,
                raw_response = responseString,
                message = "Available voices for your HeyGen account"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = ex.Message,
                message = "Failed to get voices from HeyGen API"
            });
        }
    }
}
