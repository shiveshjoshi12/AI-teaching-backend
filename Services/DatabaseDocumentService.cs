using Microsoft.EntityFrameworkCore;
using AI_driven_teaching_platform.Data;
using AI_driven_teaching_platform.Models;

namespace AI_driven_teaching_platform.Services
{
    public class DatabaseDocumentService : IDatabaseDocumentService
    {
        private readonly ApplicationDbContext _context;

        public DatabaseDocumentService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Document> CreateDocumentAsync(string title, string fileName, string contentType,
            long fileSize, string uploadedBy, string subject, string grade)
        {
            var document = new Document
            {
                Title = title,
                FileName = fileName,
                ContentType = contentType,
                FileSize = fileSize,
                UploadedBy = uploadedBy,
                Subject = subject,
                Grade = grade,
                UploadedAt = DateTime.UtcNow,
                ProcessingStatus = "pending"
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();
            return document;
        }

        public async Task<List<Document>> GetUserDocumentsAsync(string userId)
        {
            return await _context.Documents
                .Where(d => d.UploadedBy == userId)
                .OrderByDescending(d => d.UploadedAt)
                .Select(d => new Document
                {
                    Id = d.Id,
                    Title = d.Title,
                    Subject = d.Subject,
                    Grade = d.Grade,
                    ProcessingStatus = d.ProcessingStatus,
                    UploadedAt = d.UploadedAt,
                    FileSize = d.FileSize,
                    TotalChunks = d.Chunks.Count()
                })
                .ToListAsync();
        }

        public async Task<Document?> GetDocumentByIdAsync(string documentId)
        {
            return await _context.Documents
                .Include(d => d.Chunks)
                .Include(d => d.Uploader)
                .FirstOrDefaultAsync(d => d.Id == documentId);
        }

        public async Task UpdateProcessingStatusAsync(string documentId, string status, string? error = null)
        {
            var document = await _context.Documents.FindAsync(documentId);
            if (document != null)
            {
                document.ProcessingStatus = status;
                document.ProcessingError = error; // ✅ Now exists
                if (status == "completed")
                {
                    document.ProcessedAt = DateTime.UtcNow; // ✅ Now exists
                }
                await _context.SaveChangesAsync();
            }
        }

        public async Task<DocumentChunk> AddChunkAsync(string documentId, string content, int chunkIndex, ulong qdrantPointId)
        {
            var chunk = new DocumentChunk
            {
                DocumentId = documentId,
                Content = content,
                ChunkIndex = chunkIndex,
                QdrantPointId = qdrantPointId,
                StartPosition = 0, // ✅ Now exists
                EndPosition = content.Length, // ✅ Now exists
                CreatedAt = DateTime.UtcNow
            };

            _context.DocumentChunks.Add(chunk);

            // Update document chunk count
            var document = await _context.Documents.FindAsync(documentId);
            if (document != null)
            {
                document.TotalChunks = await _context.DocumentChunks.CountAsync(c => c.DocumentId == documentId) + 1;
            }

            await _context.SaveChangesAsync();
            return chunk;
        }

        public async Task<List<DocumentChunk>> GetDocumentChunksAsync(string documentId)
        {
            return await _context.DocumentChunks
                .Where(c => c.DocumentId == documentId)
                .OrderBy(c => c.ChunkIndex)
                .ToListAsync();
        }

        public async Task<bool> DeleteDocumentAsync(string documentId)
        {
            var document = await _context.Documents
                .Include(d => d.Chunks)
                .FirstOrDefaultAsync(d => d.Id == documentId);

            if (document == null) return false;

            _context.Documents.Remove(document);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task SaveDocumentChunkAsync(DocumentChunk chunk)
        {
            _context.DocumentChunks.Add(chunk);
            await _context.SaveChangesAsync();
        }
    }
}
