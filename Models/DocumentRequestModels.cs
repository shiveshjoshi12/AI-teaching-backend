using Microsoft.AspNetCore.Http;

namespace AI_driven_teaching_platform.Models
{
    // ===== REQUEST MODELS =====

    public class DocumentUploadRequest
    {
        public IFormFile File { get; set; } = null!;
        public string UserId { get; set; } = "anonymous";
        public string Subject { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
    }

    public class DocumentQuestionRequest
    {
        public string Question { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public string Language { get; set; } = "auto";
    }

    public class BatchQuestionRequest
    {
        public string DocumentId { get; set; } = string.Empty;
        public List<string> Questions { get; set; } = new();
        public string Language { get; set; } = "auto";
    }

    // ===== RESPONSE MODELS =====

    public class DocumentProcessResult
    {
        public string DocumentId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ContentPreview { get; set; } = string.Empty;
        public int ChunkCount { get; set; }
        public string ProcessingStatus { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
    }

    public class DocumentInfo
    {
        public string DocumentId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public string UploadedAt { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public int ChunkCount { get; set; }
        public string ContentPreview { get; set; } = string.Empty;
        public string ProcessingStatus { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
    }

    public class DocumentQuestionResponse
    {
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public string DocumentId { get; set; } = string.Empty;
        public string DocumentTitle { get; set; } = string.Empty;
        public bool UsedRAG { get; set; }
        public double? SearchScore { get; set; }
        public List<string> SourceChunks { get; set; } = new();
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }

    public class BatchQuestionResponse
    {
        public string DocumentId { get; set; } = string.Empty;
        public string DocumentTitle { get; set; } = string.Empty;
        public List<DocumentQuestionResponse> Results { get; set; } = new();
        public int TotalQuestions { get; set; }
        public int SuccessfulAnswers { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }

    // ===== UTILITY MODELS =====

    public class DocumentSearchRequest
    {
        public string Query { get; set; } = string.Empty;
        public string? Subject { get; set; }
        public string? Grade { get; set; }
        public int Limit { get; set; } = 5;
        public double ScoreThreshold { get; set; } = 0.3;
    }

    public class DocumentSearchResult
    {
        public string DocumentId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public double Score { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
    }
}
