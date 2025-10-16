using AI_driven_teaching_platform.Data;
using AI_driven_teaching_platform.Models;
using Microsoft.EntityFrameworkCore;

namespace AI_driven_teaching_platform.Services
{
    public interface IChatService
    {
        Task<ChatSession> CreateOrGetSessionAsync(string userId, string title = "New Chat");
        Task<ChatMessage> SaveMessageAsync(string sessionId, string question, string answer, string questionLanguage = "en", string answerLanguage = "en", bool usedRAG = false, double? searchScore = null);
        Task<List<ChatSession>> GetUserSessionsAsync(string userId);
        Task<List<ChatMessage>> GetSessionMessagesAsync(string sessionId);
    }

    public class ChatService : IChatService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ChatService> _logger;

        public ChatService(ApplicationDbContext context, ILogger<ChatService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ChatSession> CreateOrGetSessionAsync(string userId, string title = "New Chat")
        {
            try
            {
                // Get or create active session for user
                var activeSession = await _context.ChatSessions
                    .Where(s => s.UserId == userId && s.IsActive)
                    .OrderByDescending(s => s.UpdatedAt)
                    .FirstOrDefaultAsync();

                if (activeSession != null)
                {
                    return activeSession;
                }

                // Create new session
                var newSession = new ChatSession
                {
                    Id = Guid.NewGuid().ToString(),
                    UserId = userId,
                    Title = title,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                await _context.ChatSessions.AddAsync(newSession);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Created new chat session: {newSession.Id}");
                return newSession;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating/getting chat session");
                throw;
            }
        }

        public async Task<ChatMessage> SaveMessageAsync(
            string sessionId,
            string question,
            string answer,
            string questionLanguage = "en",
            string answerLanguage = "en",
            bool usedRAG = false,
            double? searchScore = null)
        {
            try
            {
                var message = new ChatMessage
                {
                    Id = Guid.NewGuid().ToString(),
                    SessionId = sessionId,
                    Question = question,
                    Answer = answer,
                    QuestionLanguage = questionLanguage,
                    AnswerLanguage = answerLanguage,
                    UsedRAG = usedRAG,
                    SearchScore = searchScore,
                    CreatedAt = DateTime.UtcNow
                };

                await _context.ChatMessages.AddAsync(message);

                // Update session timestamp
                var session = await _context.ChatSessions.FindAsync(sessionId);
                if (session != null)
                {
                    session.UpdatedAt = DateTime.UtcNow;
                    // Auto-generate title from first message
                    if (session.Title == "New Chat" || session.Title.StartsWith("Chat "))
                    {
                        session.Title = question.Length > 50 ? question.Substring(0, 50) + "..." : question;
                    }
                }

                await _context.SaveChangesAsync();

                _logger.LogInformation($"Saved chat message to session: {sessionId}");
                return message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving chat message");
                throw;
            }
        }

        public async Task<List<ChatSession>> GetUserSessionsAsync(string userId)
        {
            return await _context.ChatSessions
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.UpdatedAt)
                .Include(s => s.Messages)
                .ToListAsync();
        }

        public async Task<List<ChatMessage>> GetSessionMessagesAsync(string sessionId)
        {
            return await _context.ChatMessages
                .Where(m => m.SessionId == sessionId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();
        }
    }
}
