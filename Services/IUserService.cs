using GPIMSWebServer.Data;
using GPIMSWebServer.Models;

namespace GPIMSWebServer.Services
{
    public interface IUserService
    {
        Task<User?> AuthenticateAsync(string username, string password);
        Task<List<User>> GetAllUsersAsync();
        Task<User?> GetUserByIdAsync(int id);
        Task<User?> GetUserByUsernameAsync(string username);
        Task<bool> CreateUserAsync(UserViewModel userViewModel);
        Task<bool> UpdateUserAsync(UserViewModel userViewModel);
        Task<bool> DeleteUserAsync(int id);
        Task<bool> UsernameExistsAsync(string username);
        string HashPassword(string password);
        bool VerifyPassword(string password, string hash);
    }
}