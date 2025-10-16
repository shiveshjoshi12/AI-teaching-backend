using Microsoft.EntityFrameworkCore;
using AI_driven_teaching_platform.Models;

namespace AI_driven_teaching_platform.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<UserPreferences> UserPreferences { get; set; }
        public DbSet<ChatSession> ChatSessions { get; set; }
        public DbSet<ChatMessage> ChatMessages { get; set; }
        public DbSet<Document> Documents { get; set; }
        public DbSet<DocumentChunk> DocumentChunks { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Email).IsUnique();
                entity.HasIndex(e => e.GoogleId).IsUnique();
            });

            // One-to-One relationship: User -> UserPreferences
            modelBuilder.Entity<UserPreferences>(entity =>
            {
                entity.HasOne(e => e.User)
                    .WithOne(u => u.Preferences)
                    .HasForeignKey<UserPreferences>(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // One-to-Many: User -> ChatSessions
            modelBuilder.Entity<ChatSession>(entity =>
            {
                entity.HasOne(e => e.User)
                    .WithMany(u => u.ChatSessions)
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // One-to-Many: ChatSession -> ChatMessages
            modelBuilder.Entity<ChatMessage>(entity =>
            {
                entity.HasOne(e => e.Session)
                    .WithMany(s => s.Messages)
                    .HasForeignKey(e => e.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // One-to-Many: User -> Documents
            modelBuilder.Entity<Document>(entity =>
            {
                entity.HasOne(e => e.Uploader)
                    .WithMany(u => u.UploadedDocuments)
                    .HasForeignKey(e => e.UploadedBy)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // One-to-Many: Document -> DocumentChunks
            modelBuilder.Entity<DocumentChunk>(entity =>
            {
                entity.HasOne(e => e.Document)
                    .WithMany(d => d.Chunks)
                    .HasForeignKey(e => e.DocumentId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.QdrantPointId).IsUnique();
            });
        }
    }
}
