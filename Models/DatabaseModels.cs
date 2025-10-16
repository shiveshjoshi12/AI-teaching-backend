using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AI_driven_teaching_platform.Models
{
    [Table("Users")]
    public class User
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string GoogleId { get; set; } = string.Empty;

        [Required]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Name { get; set; } = string.Empty;

        public string Picture { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastLoginAt { get; set; } = DateTime.UtcNow;

        public string Role { get; set; } = "Student";
        public bool IsActive { get; set; } = true;

        // Navigation Properties
        public virtual UserPreferences? Preferences { get; set; }
        public virtual ICollection<ChatSession> ChatSessions { get; set; } = new List<ChatSession>();
        public virtual ICollection<Document> UploadedDocuments { get; set; } = new List<Document>();
    }

    [Table("UserPreferences")]
    public class UserPreferences
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string UserId { get; set; } = string.Empty;

        public string PreferredLanguage { get; set; } = "en";
        public string PreferredSubject { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public string InterestedTopics { get; set; } = "[]";
        public bool EnableNotifications { get; set; } = true;
        public bool EnableDarkMode { get; set; } = false;
        public bool EnableSoundEffects { get; set; } = true; // 👈 MISSING PROPERTY
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;
    }

    [Table("ChatSessions")]
    public class ChatSession
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string UserId { get; set; } = string.Empty;

        [Required]
        public string Title { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;

        [ForeignKey("UserId")]
        public virtual User User { get; set; } = null!;
        public virtual ICollection<ChatMessage> Messages { get; set; } = new List<ChatMessage>();
    }

    [Table("ChatMessages")]
    public class ChatMessage
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string SessionId { get; set; } = string.Empty;

        [Required]
        public string Question { get; set; } = string.Empty;

        [Required]
        public string Answer { get; set; } = string.Empty;

        public string QuestionLanguage { get; set; } = "en";
        public string AnswerLanguage { get; set; } = "en";
        public bool UsedRAG { get; set; } = false;
        public double? SearchScore { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("SessionId")]
        public virtual ChatSession Session { get; set; } = null!;
    }

    [Table("Documents")]
    public class Document
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string FileName { get; set; } = string.Empty;

        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FilePath { get; set; } = string.Empty;

        [Required]
        public string UploadedBy { get; set; } = string.Empty;

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public string Subject { get; set; } = string.Empty;
        public string Grade { get; set; } = string.Empty;
        public string ProcessingStatus { get; set; } = "pending";
        public string? ProcessingError { get; set; } = null; // 👈 MISSING PROPERTY
        public DateTime? ProcessedAt { get; set; } = null; // 👈 MISSING PROPERTY
        public int TotalChunks { get; set; } = 0;

        [ForeignKey("UploadedBy")]
        public virtual User Uploader { get; set; } = null!;
        public virtual ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
    }

    [Table("DocumentChunks")]
    public class DocumentChunk
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string DocumentId { get; set; } = string.Empty;

        [Required]
        public string Content { get; set; } = string.Empty;

        public int ChunkIndex { get; set; }
        public int StartPosition { get; set; } = 0; // 👈 MISSING PROPERTY
        public int EndPosition { get; set; } = 0; // 👈 MISSING PROPERTY
        public ulong QdrantPointId { get; set; } // Bridge to Qdrant
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [ForeignKey("DocumentId")]
        public virtual Document Document { get; set; } = null!;
    }
}
