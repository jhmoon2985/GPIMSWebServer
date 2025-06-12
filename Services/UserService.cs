using Microsoft.EntityFrameworkCore;
using GPIMSWebServer.Data;
using GPIMSWebServer.Models;
using BCrypt.Net;

namespace GPIMSWebServer.Services
{
    public class UserService : IUserService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<UserService> _logger;

        public UserService(ApplicationDbContext context, ILogger<UserService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<User?> AuthenticateAsync(string username, string password)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

                if (user != null && VerifyPassword(password, user.PasswordHash))
                {
                    _logger.LogInformation($"User {username} authenticated successfully");
                    return user;
                }

                _logger.LogWarning($"Authentication failed for user {username}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error authenticating user {username}");
                return null;
            }
        }

        public async Task<List<User>> GetAllUsersAsync()
        {
            return await _context.Users.OrderBy(u => u.Username).ToListAsync();
        }

        public async Task<User?> GetUserByIdAsync(int id)
        {
            return await _context.Users.FindAsync(id);
        }

        public async Task<User?> GetUserByUsernameAsync(string username)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
        }

        public async Task<bool> CreateUserAsync(UserViewModel userViewModel)
        {
            try
            {
                if (string.IsNullOrEmpty(userViewModel.Password))
                {
                    return false;
                }

                var user = new User
                {
                    Username = userViewModel.Username,
                    PasswordHash = HashPassword(userViewModel.Password),
                    Name = userViewModel.Name,
                    Department = userViewModel.Department,
                    Role = userViewModel.Role,
                    IsActive = userViewModel.IsActive,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();
                
                _logger.LogInformation($"User {userViewModel.Username} created successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating user {userViewModel.Username}");
                return false;
            }
        }

        public async Task<bool> UpdateUserAsync(UserViewModel userViewModel)
        {
            try
            {
                var user = await _context.Users.FindAsync(userViewModel.Id);
                if (user == null) return false;

                user.Username = userViewModel.Username;
                user.Name = userViewModel.Name;
                user.Department = userViewModel.Department;
                user.Role = userViewModel.Role;
                user.IsActive = userViewModel.IsActive;
                user.UpdatedAt = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(userViewModel.Password))
                {
                    user.PasswordHash = HashPassword(userViewModel.Password);
                }

                await _context.SaveChangesAsync();
                
                _logger.LogInformation($"User {userViewModel.Username} updated successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating user {userViewModel.Username}");
                return false;
            }
        }

        public async Task<bool> DeleteUserAsync(int id)
        {
            try
            {
                var user = await _context.Users.FindAsync(id);
                if (user == null) return false;

                user.IsActive = false;
                user.UpdatedAt = DateTime.UtcNow;
                
                await _context.SaveChangesAsync();
                
                _logger.LogInformation($"User {user.Username} deactivated successfully");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deactivating user with ID {id}");
                return false;
            }
        }

        public async Task<bool> UsernameExistsAsync(string username)
        {
            return await _context.Users.AnyAsync(u => u.Username == username);
        }

        public string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }

        public bool VerifyPassword(string password, string hash)
        {
            try
            {
                return BCrypt.Net.BCrypt.Verify(password, hash);
            }
            catch
            {
                return false;
            }
        }
    }
}