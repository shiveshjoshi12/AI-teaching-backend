using AI_driven_teaching_platform.Data;
using AI_driven_teaching_platform.Models;
using Microsoft.EntityFrameworkCore;

namespace AI_driven_teaching_platform.Services
{
    public interface IUserService
    {
        Task<User> CreateOrUpdateUserAsync(string googleId, string email, string name, string picture);
        Task<User?> GetUserByEmailAsync(string email);
        Task<User?> GetUserByIdAsync(string userId);
    }

    public class UserService : IUserService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<UserService> _logger;

        public UserService(ApplicationDbContext context, ILogger<UserService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<User> CreateOrUpdateUserAsync(string googleId, string email, string name, string picture)
        {
            try
            {
                _logger.LogInformation($"CreateOrUpdateUserAsync called for email: {email}");

                // Check if user exists by email
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == email);

                if (existingUser != null)
                {
                    _logger.LogInformation($"User found, updating: {email}");
                    
                    // Update existing user
                    existingUser.Name = name;
                    existingUser.Picture = picture;
                    existingUser.LastLoginAt = DateTime.UtcNow;
                    
                    _context.Users.Update(existingUser);
                    await _context.SaveChangesAsync();
                    
                    _logger.LogInformation($"User updated successfully: {email}");
                    return existingUser;
                }
                else
                {
                    _logger.LogInformation($"User not found, creating new user: {email}");
                    
                    // Create new user
                    var newUser = new User
                    {
                        Id = Guid.NewGuid().ToString(),
                        GoogleId = googleId,
                        Email = email,
                        Name = name,
                        Picture = picture ?? "https://via.placeholder.com/150",
                        Role = "Student",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        LastLoginAt = DateTime.UtcNow
                    };

                    await _context.Users.AddAsync(newUser);
                    await _context.SaveChangesAsync();
                    
                    _logger.LogInformation($"New user created successfully: {email}, ID: {newUser.Id}");
                    return newUser;
                }
            }
            catch (DbUpdateException dbEx)
            {
                _logger.LogError(dbEx, $"Database error in CreateOrUpdateUserAsync for {email}");
                
                // Log inner exceptions
                var innerEx = dbEx.InnerException;
                while (innerEx != null)
                {
                    _logger.LogError($"Inner exception: {innerEx.Message}");
                    innerEx = innerEx.InnerException;
                }
                
                throw new Exception($"Failed to save user to database: {dbEx.InnerException?.Message ?? dbEx.Message}", dbEx);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected error in CreateOrUpdateUserAsync for {email}");
                throw;
            }
        }

        public async Task<User?> GetUserByEmailAsync(string email)
        {
            try
            {
                return await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting user by email: {email}");
                return null;
            }
        }

        public async Task<User?> GetUserByIdAsync(string userId)
        {
            try
            {
                return await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting user by ID: {userId}");
                return null;
            }
        }
    }
}
