using Blazor.RCL.Application.Common.Configuration.Interfaces;
using Blazor.RCL.Infrastructure.Authentication.Interfaces;
using Blazor.RCL.Infrastructure.Common.Configuration;
using Blazor.RCL.Infrastructure.Services.Interfaces;
using Blazor.RCL.NLog.LogService.Interface;
using Blazor.RCL.Application.Interfaces;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Text;
using Blazor.RCL.Application.Models;

namespace Blazor.Identity.Controllers
{
    /// <summary>
    /// Controller responsible for handling authentication-related operations.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("/")]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public class AuthenticationController : ControllerBase
    {
        #region Private Fields
        private readonly IDomainUserGroupService _domainUserGroupService;
        private readonly ILogHelper _logger;
        private readonly IAppConfiguration _appConfiguration;
        private readonly IDistributedCache _cache;
        #endregion

        #region Constructor
        public AuthenticationController( IDomainUserGroupService domainUserGroupService, ILogHelper logger, IAppConfiguration appConfiguration,IDistributedCache cache)
        {
            _domainUserGroupService = domainUserGroupService;
            _logger = logger;
            _appConfiguration = appConfiguration;
            _cache = cache;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Handles user login and redirects to the specified return URL.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("login")]
        public async Task<IActionResult> Login(string returnUrl)
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                await AuthenticateUser();
                return Redirect(returnUrl);
            }
            else
            {
                return Unauthorized();
            }
        }

        /// <summary>
        /// Handles cookie-based login and redirects to the authentication API URL.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("cookielogin")]
        public async Task<IActionResult> CookieLogin()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                await AuthenticateUser();
                return Redirect(_appConfiguration.AuthenticationAPIURL);
            }
            else
            {
                return Unauthorized();
            }
        }

        /// <summary>
        /// Logs out the user and redirects to the specified return URL.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("logout")]
        public async Task<IActionResult> Logout(string returnUrl)
        {
            if (User.Identity!.IsAuthenticated == true)
            {
                var username = User.Identity.Name;
                await _cache.RemoveAsync($"user_roles_{username}");
            }

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect(returnUrl);
        }

        /// <summary>
        /// Updates the roles for the current user.
        /// </summary>
        [HttpPost("updateroles")]
        public async Task<IActionResult> UpdateRoles([FromBody] RoleUpdateRequest request)
        {
            if (request.Roles == null)
            {
                return BadRequest("Roles cannot be null");
            }

            try
            {
                var (newPrincipal, updatedRoles) = await UpdateUserRoles(request.Roles);

                await SignInWithNewPrincipal(newPrincipal);

                return Ok(new
                {
                    Message = "Roles updated successfully",
                    Roles = updatedRoles
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("Role Update Error", ex);
                return StatusCode(StatusCodes.Status500InternalServerError, "An error occurred while updating roles.");
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Authenticates the user by creating and storing necessary claims and roles.
        /// </summary>
        private async Task AuthenticateUser()
        {
            var username = User.Identity!.Name;
            var lastAuthenticated = DateTime.UtcNow;
            var roles = _domainUserGroupService.GetRolesFromGroups(User);

            var claims = _domainUserGroupService.CreateClaims(username!, roles, lastAuthenticated);

            await CacheUserRoles(username!, roles);

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            var authenticationProperties = new AuthenticationProperties()
            {
                IsPersistent = true,
                ExpiresUtc = lastAuthenticated.AddHours(_appConfiguration.SessionTimeout)
            };

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authenticationProperties);
        }

        /// <summary>
        /// Caches the user roles in the distributed cache.
        /// </summary>
        private async Task CacheUserRoles(string username, List<string> roles)
        {
            string serializedRoles = JsonSerializer.Serialize(roles);
            await _cache.SetAsync(
                $"user_roles_{username}",
                Encoding.UTF8.GetBytes(serializedRoles),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(_appConfiguration.SessionTimeout),
                    SlidingExpiration = TimeSpan.FromMinutes(30)
                });
        }

        /// <summary>
        /// Updates the user roles and returns a new ClaimsPrincipal with updated roles.
        /// </summary>
        private async Task<(ClaimsPrincipal, List<string>)> UpdateUserRoles(List<string> newRoles)
        {
            var identity = (ClaimsIdentity)User.Identity!;
            var username = identity.Name!;

            var existingRoles = identity.FindAll(ClaimTypes.Role).Select(c => c.Value).ToHashSet();
            existingRoles.UnionWith(newRoles);

            var updatedRoles = existingRoles.ToList();

            var newClaims = identity.Claims
                .Where(c => c.Type != ClaimTypes.Role)
                .Concat(updatedRoles.Select(r => new Claim(ClaimTypes.Role, r)))
                .ToList();

            var newIdentity = new ClaimsIdentity(newClaims, identity.AuthenticationType);
            var newPrincipal = new ClaimsPrincipal(newIdentity);

            await CacheUserRoles(username, updatedRoles);

            return (newPrincipal, updatedRoles);
        }

        /// <summary>
        /// Signs in the user with the new ClaimsPrincipal.
        /// </summary>
        private async Task SignInWithNewPrincipal(ClaimsPrincipal principal)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTime.UtcNow.AddHours(_appConfiguration.SessionTimeout)
                });
        }
        #endregion
    }
}