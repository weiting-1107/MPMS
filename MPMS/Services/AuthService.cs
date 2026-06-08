using System;
using System.Threading.Tasks;
using BCrypt.Net;
using MPMS.Models;
using MPMS.Repositories;

namespace MPMS.Services
{
    public class AuthService
    {
        private readonly UserRepository _userRepository;

        public AuthService(UserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        /// <summary>
        /// Authenticates a user with their account and password.
        /// </summary>
        public async Task<User?> AuthenticateAsync(string account, string password)
        {
            if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(password))
            {
                return null;
            }

            var user = await _userRepository.GetUserByAccountAsync(account.Trim());
            if (user == null || !user.IsActive)
            {
                return null;
            }

            // Verify password using BCrypt
            try
            {
                bool isPasswordValid = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
                if (!isPasswordValid)
                {
                    return null;
                }
            }
            catch
            {
                // In case of hashing issues (e.g. invalid format)
                return null;
            }

            // Update last login timestamp
            await _userRepository.UpdateLastLoginAsync(user.UserId);

            return user;
        }

        /// <summary>
        /// Hashes a plaintext password using BCrypt.
        /// </summary>
        public string HashPassword(string password)
        {
            return BCrypt.Net.BCrypt.HashPassword(password);
        }
    }
}
