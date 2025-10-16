using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options; // ← IMPORTANT: Add this using
using AI_driven_teaching_platform.Models;

namespace AI_driven_teaching_platform.Services
{
    public class HeyGenService
    {
        private readonly HttpClient _httpClient;
        private readonly HeyGenSettings _settings;

        // FIXED CONSTRUCTOR - This is the key change
        public HeyGenService(HttpClient httpClient, IOptions<HeyGenSettings> options)
        {
            _httpClient = httpClient;
            _settings = options.Value;
        }

        public string GetApiKey()
        {
            return _settings.ApiKey;
        }

        public async Task<string> TestConnectionDetailedAsync()
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("HEYGEN_API_KEY") ?? _settings.ApiKey;

                if (string.IsNullOrEmpty(apiKey))
                {
                    return "No API key found in environment or settings";
                }

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);

                // Try a simple endpoint first
                var response = await _httpClient.GetAsync($"{_settings.BaseUrl}/user");
                var content = await response.Content.ReadAsStringAsync();

                return $"Status: {response.StatusCode}, Response: {content}";
            }
            catch (Exception ex)
            {
                return $"Connection test failed: {ex.Message}";
            }
        }


        public async Task<VideoGenerationResponse> CreateVideoAsync(CreateVideoRequest request)
        {
            try
            {
                var heygenRequest = new
                {
                    title = request.Title ?? "AI Generated Video",
                    video_inputs = new[]
    {
        new
        {
            character = new
            {
                type = "avatar",
                avatar_id = request.AvatarId ?? "Tyler-insuit-20220721"
            },
            voice = new
            {
                type = "text",
                input_text = request.Text,
                voice_id = request.Voice ?? "1bd001e7e50f421d891986aad5158bc8"
            }
        }
    },
                    background = new
                    {
                        type = "color",
                        value = "#ffffff"
                    },
                    aspect_ratio = "16:9"
                };



                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var jsonContent = JsonSerializer.Serialize(heygenRequest);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_settings.BaseUrl}/video/generate", content);
                var responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var heygenResponse = JsonSerializer.Deserialize<JsonElement>(responseString);

                    string videoId = "";
                    if (heygenResponse.TryGetProperty("data", out var data) &&
                        data.TryGetProperty("video_id", out var vidId))
                    {
                        videoId = vidId.GetString() ?? "";
                    }

                    return new VideoGenerationResponse
                    {
                        VideoId = videoId,
                        Status = "processing",
                        CreatedAt = DateTime.UtcNow,
                        AvatarUsed = request.AvatarId ?? "Tyler-insuit-20220721",
                        TextContent = request.Text ?? ""
                    };
                }
                else
                {
                    throw new Exception($"HeyGen API Error: {response.StatusCode} - {responseString}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Video generation failed: {ex.Message}");
            }
        }

        public async Task<VideoStatusResponse> GetVideoStatusAsync(string videoId)
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

                var response = await _httpClient.GetAsync($"{_settings.BaseUrl}/video/{videoId}");
                var responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var heygenResponse = JsonSerializer.Deserialize<HeyGenApiResponse>(responseString);

                    return new VideoStatusResponse
                    {
                        VideoId = videoId,
                        Status = heygenResponse?.data?.status ?? "unknown",
                        VideoUrl = heygenResponse?.data?.video_url,
                        ThumbnailUrl = heygenResponse?.data?.thumbnail_url,
                        Progress = CalculateProgress(heygenResponse?.data?.status ?? "unknown")
                    };
                }
                else
                {
                    return new VideoStatusResponse
                    {
                        VideoId = videoId,
                        Status = "error",
                        ErrorMessage = $"API Error: {response.StatusCode}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new VideoStatusResponse
                {
                    VideoId = videoId,
                    Status = "error",
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<List<AvatarInfo>> GetAvailableAvatarsAsync()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                var response = await _httpClient.GetAsync($"{_settings.BaseUrl}/avatars");
                var responseString = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var avatarList = new List<AvatarInfo>();

                    using var doc = JsonDocument.Parse(responseString);

                    // Parse actual HeyGen response
                    if (doc.RootElement.TryGetProperty("data", out var data) &&
                        data.TryGetProperty("avatars", out var avatars))
                    {
                        foreach (var avatar in avatars.EnumerateArray())
                        {
                            var avatarInfo = new AvatarInfo
                            {
                                Id = avatar.TryGetProperty("avatar_id", out var id) ? id.GetString() ?? "" : "",
                                Name = avatar.TryGetProperty("avatar_name", out var name) ? name.GetString() ?? "" : "",
                                ThumbnailUrl = avatar.TryGetProperty("preview_image_url", out var thumb) ? thumb.GetString() ?? "" : "",
                                Gender = avatar.TryGetProperty("gender", out var gender) ? gender.GetString() ?? "" : "",
                                SupportedLanguages = new List<string>()
                            };

                            if (!string.IsNullOrEmpty(avatarInfo.Id))
                            {
                                avatarList.Add(avatarInfo);
                            }
                        }
                    }

                    return avatarList.Count > 0 ? avatarList : GetFallbackAvatars(responseString);
                }
                else
                {
                    throw new Exception($"Failed to get avatars: {response.StatusCode} - {responseString}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting avatars: {ex.Message}");
                return GetFallbackAvatars($"Error: {ex.Message}");
            }
        }

        private List<AvatarInfo> GetFallbackAvatars(string debugInfo)
        {
            return new List<AvatarInfo>
    {
        new AvatarInfo
        {
            Id = "debug_info",
            Name = "Debug Info",
            Gender = "Debug",
            ThumbnailUrl = debugInfo
        }
    };
        }


        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var apiKey = Environment.GetEnvironmentVariable("HEYGEN_API_KEY") ?? _settings.ApiKey;

                if (string.IsNullOrEmpty(apiKey))
                {
                    return false;
                }

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

                // Use the WORKING endpoint (same as quota check)
                var response = await _httpClient.GetAsync($"{_settings.BaseUrl}/user/remaining_quota");

                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private int CalculateProgress(string status)
        {
            return status.ToLower() switch
            {
                "pending" => 0,
                "processing" => 50,
                "completed" => 100,
                "failed" => 0,
                _ => 25
            };
        }
    }
}
