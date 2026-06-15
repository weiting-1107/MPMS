using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.Controllers
{
    public class AccountController : Controller
    {
        private readonly AuthService _authService;
        private readonly Repositories.UserRepository _userRepository;

        public AccountController(AuthService authService, Repositories.UserRepository userRepository)
        {
            _authService = authService;
            _userRepository = userRepository;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Dashboard");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _authService.AuthenticateAsync(model.Account, model.Password);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "帳號或密碼錯誤，或帳號已被停用。");
                return View(model);
            }

            // Create user claims for cookie session
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
                new Claim(ClaimTypes.Name, user.Account),
                new Claim("UserName", user.UserName),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.RoleCode),
                new Claim("RoleName", user.RoleName)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(4)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Index", "Dashboard");
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // GET: Account/ChangePassword
        [HttpGet]
        [Authorize]
        public IActionResult ChangePassword()
        {
            return View(new ChangePasswordViewModel());
        }

        // POST: Account/ChangePassword
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var account = User.Identity?.Name;
            if (string.IsNullOrEmpty(account))
            {
                return RedirectToAction("Login");
            }

            // Verify old password
            var user = await _authService.AuthenticateAsync(account, model.OldPassword);
            if (user == null)
            {
                ModelState.AddModelError("OldPassword", "舊密碼不正確。");
                return View(model);
            }

            // Hash new password and update
            string newHash = _authService.HashPassword(model.NewPassword);
            bool success = await _userRepository.UpdatePasswordAsync(user.UserId, newHash);

            if (success)
            {
                TempData["SuccessMessage"] = "密碼已成功變更！下次請使用新密碼登入。";
                return RedirectToAction("Index", "Dashboard");
            }

            ModelState.AddModelError(string.Empty, "密碼變更失敗，請稍後再試。");
            return View(model);
        }
    }
}
