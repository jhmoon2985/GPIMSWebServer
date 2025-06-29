using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GPIMSWebServer.Models;
using GPIMSWebServer.Services;
using System.Security.Claims;

namespace GPIMSWebServer.Controllers
{
    [Authorize]
    public class UserController : Controller
    {
        private readonly IUserService _userService;
        private readonly IUserActivityService _activityService;
        private readonly ILogger<UserController> _logger;

        public UserController(IUserService userService, IUserActivityService activityService, ILogger<UserController> logger)
        {
            _userService = userService;
            _activityService = activityService;
            _logger = logger;
        }

        // GET: User
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> Index()
        {
            try
            {
                var users = await _userService.GetAllUsersAsync();
                return View(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving users list");
                TempData["Error"] = "Error loading users list.";
                return View(new List<User>());
            }
        }

        // GET: User/Activities - 모든 사용자 활동 보기 (관리자만)
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> Activities(int count = 100)
        {
            try
            {
                var activities = await _activityService.GetAllActivitiesAsync(count);
                ViewBag.Count = count;
                return View(activities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user activities");
                TempData["Error"] = "Error loading user activities.";
                return View(new List<UserActivityViewModel>());
            }
        }

        // GET: User/UserActivities/5 - 특정 사용자 활동 보기
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> UserActivities(int id, int count = 50)
        {
            try
            {
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    TempData["Error"] = "User not found.";
                    return RedirectToAction(nameof(Index));
                }

                var activities = await _activityService.GetUserActivitiesAsync(id, count);
                ViewBag.User = user;
                ViewBag.Count = count;
                return View(activities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving activities for user {id}");
                TempData["Error"] = "Error loading user activities.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: User/MyActivities - 내 활동 기록 보기
        [Authorize]
        public async Task<IActionResult> MyActivities(int count = 50)
        {
            try
            {
                var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var activities = await _activityService.GetUserActivitiesAsync(currentUserId, count);
                ViewBag.Count = count;
                return View(activities);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user's own activities");
                TempData["Error"] = "Error loading your activities.";
                return View(new List<UserActivityViewModel>());
            }
        }

        // GET: User/Create
        [Authorize(Policy = "AdminOnly")]
        public IActionResult Create()
        {
            return View();
        }

        // POST: User/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> Create(UserViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                // Check if username already exists
                if (await _userService.UsernameExistsAsync(model.Username))
                {
                    ModelState.AddModelError("Username", "Username already exists.");
                    return View(model);
                }

                var result = await _userService.CreateUserAsync(model);
                if (result)
                {
                    // 사용자 생성 활동 기록
                    await LogUserActivity(ActivityType.CreateUser, $"Created user: {model.Username}");
                    
                    TempData["Success"] = $"User '{model.Username}' created successfully.";
                    return RedirectToAction(nameof(Index));
                }
                else
                {
                    ModelState.AddModelError("", "Failed to create user. Please try again.");
                    return View(model);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating user {model.Username}");
                ModelState.AddModelError("", "An error occurred while creating the user.");
                return View(model);
            }
        }

        // GET: User/Edit/5
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> Edit(int id)
        {
            try
            {
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    TempData["Error"] = "User not found.";
                    return RedirectToAction(nameof(Index));
                }

                var model = new UserViewModel
                {
                    Id = user.Id,
                    Username = user.Username,
                    Name = user.Name,
                    Department = user.Department,
                    Role = user.Role,
                    IsActive = user.IsActive
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving user with ID {id}");
                TempData["Error"] = "Error loading user details.";
                return RedirectToAction(nameof(Index));
            }
        }

        // POST: User/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> Edit(int id, UserViewModel model)
        {
            if (id != model.Id)
            {
                TempData["Error"] = "Invalid user ID.";
                return RedirectToAction(nameof(Index));
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                // Check if username already exists (excluding current user)
                var existingUser = await _userService.GetUserByUsernameAsync(model.Username);
                if (existingUser != null && existingUser.Id != model.Id)
                {
                    ModelState.AddModelError("Username", "Username already exists.");
                    return View(model);
                }

                var result = await _userService.UpdateUserAsync(model);
                if (result)
                {
                    // 사용자 수정 활동 기록
                    await LogUserActivity(ActivityType.EditUser, $"Updated user: {model.Username}");
                    
                    TempData["Success"] = $"User '{model.Username}' updated successfully.";
                    return RedirectToAction(nameof(Index));
                }
                else
                {
                    ModelState.AddModelError("", "Failed to update user. Please try again.");
                    return View(model);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating user {model.Username}");
                ModelState.AddModelError("", "An error occurred while updating the user.");
                return View(model);
            }
        }

        // POST: User/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var user = await _userService.GetUserByIdAsync(id);
                if (user == null)
                {
                    TempData["Error"] = "User not found.";
                    return RedirectToAction(nameof(Index));
                }

                // Prevent admin from deactivating themselves
                var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                if (id == currentUserId)
                {
                    TempData["Error"] = "You cannot deactivate your own account.";
                    return RedirectToAction(nameof(Index));
                }

                var result = await _userService.DeleteUserAsync(id);
                if (result)
                {
                    // 사용자 삭제 활동 기록
                    await LogUserActivity(ActivityType.DeleteUser, $"Deactivated user: {user.Username}");
                    
                    TempData["Success"] = $"User '{user.Username}' deactivated successfully.";
                }
                else
                {
                    TempData["Error"] = "Failed to deactivate user.";
                }

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deactivating user with ID {id}");
                TempData["Error"] = "An error occurred while deactivating the user.";
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: User/Profile
        [Authorize]
        public async Task<IActionResult> Profile()
        {
            try
            {
                var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                var user = await _userService.GetUserByIdAsync(currentUserId);
                
                if (user == null)
                {
                    TempData["Error"] = "User profile not found.";
                    return RedirectToAction("Index", "Home");
                }

                var model = new UserViewModel
                {
                    Id = user.Id,
                    Username = user.Username,
                    Name = user.Name,
                    Department = user.Department,
                    Role = user.Role,
                    IsActive = user.IsActive
                };

                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading user profile");
                TempData["Error"] = "Error loading profile.";
                return RedirectToAction("Index", "Home");
            }
        }

        // POST: User/UpdateProfile
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> UpdateProfile(UserViewModel model)
        {
            try
            {
                var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
                
                if (model.Id != currentUserId)
                {
                    TempData["Error"] = "You can only update your own profile.";
                    return RedirectToAction("Profile");
                }

                // Users can only update their name and password
                var user = await _userService.GetUserByIdAsync(currentUserId);
                if (user == null)
                {
                    TempData["Error"] = "User not found.";
                    return RedirectToAction("Profile");
                }

                // Create a limited update model
                var updateModel = new UserViewModel
                {
                    Id = user.Id,
                    Username = user.Username, // Keep original username
                    Name = model.Name,
                    Department = user.Department, // Keep original department
                    Role = user.Role, // Keep original role
                    IsActive = user.IsActive, // Keep original status
                    Password = model.Password // Allow password update
                };

                if (ModelState.IsValid)
                {
                    var result = await _userService.UpdateUserAsync(updateModel);
                    if (result)
                    {
                        // 프로필 수정 활동 기록
                        await LogUserActivity(ActivityType.EditProfile, "Updated own profile");
                        
                        TempData["Success"] = "Profile updated successfully.";
                    }
                    else
                    {
                        TempData["Error"] = "Failed to update profile.";
                    }
                }

                return RedirectToAction("Profile");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating user profile");
                TempData["Error"] = "An error occurred while updating profile.";
                return RedirectToAction("Profile");
            }
        }

        // Helper method to log user activities
        private async Task LogUserActivity(ActivityType activityType, string description)
        {
            try
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var username = User.Identity?.Name;

                if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(username) && int.TryParse(userId, out int userIdInt))
                {
                    var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
                    await _activityService.LogActivityAsync(userIdInt, username, activityType, description, ipAddress);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log user activity");
            }
        }
    }
}