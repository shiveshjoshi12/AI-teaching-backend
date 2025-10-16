using AI_driven_teaching_platform.Models;

namespace AI_driven_teaching_platform.Services
{
    public interface IDatabaseDocumentService
    {
        Task<Document> CreateDocumentAsync(string title, string fileName, string contentType,
            long fileSize, string uploadedBy, string subject, string grade);
        Task<List<Document>> GetUserDocumentsAsync(string userId);
        Task<Document?> GetDocumentByIdAsync(string documentId);
        Task UpdateProcessingStatusAsync(string documentId, string status, string? error = null);
        Task<DocumentChunk> AddChunkAsync(string documentId, string content, int chunkIndex, ulong qdrantPointId);
        Task<List<DocumentChunk>> GetDocumentChunksAsync(string documentId);
        Task<bool> DeleteDocumentAsync(string documentId);
        Task SaveDocumentChunkAsync(DocumentChunk chunk);  // NEW
    }
}
