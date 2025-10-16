using Microsoft.AspNetCore.Mvc;
using AI_driven_teaching_platform.Services;
using AI_driven_teaching_platform.Models;
using Google.Apis.Auth;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;

namespace AI_driven_teaching_platform.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ILogger<AuthController> _logger;
        private readonly IConfiguration _configuration;

        public AuthController(
            IUserService userService,
            ILogger<AuthController> logger,
            IConfiguration configuration)
        {
            _userService = userService;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpPost("google")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
        {
            try
            {
                _logger.LogInformation("Google login attempt received");

                if (string.IsNullOrEmpty(request.Credential))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "No credential provided"
                    });
                }

                // ✅ Verify Google JWT token
                var payload = await VerifyGoogleToken(request.Credential);

                if (payload == null)
                {
                    return Unauthorized(new
                    {
                        success = false,
                        error = "Invalid Google token"
                    });
                }

                // ✅ Extract real user information from Google token
                var googleId = payload.Subject;
                var email = payload.Email;
                var name = payload.Name;
                var picture = payload.Picture;

                _logger.LogInformation($"Google user authenticated: {email}");

                // Create or update user in database
                var user = await _userService.CreateOrUpdateUserAsync(
                    googleId: googleId,
                    email: email,
                    name: name,
                    picture: picture
                );

                // ✅ Generate proper JWT token
                var token = GenerateJwtToken(user);

                _logger.LogInformation($"User logged in successfully: {user.Email}");

                return Ok(new
                {
                    success = true,
                    token = token,
                    user = new
                    {
                        id = user.Id,
                        name = user.Name,
                        email = user.Email,
                        picture = user.Picture,
                        role = user.Role
                    }
                });
            }
            catch (InvalidJwtException ex)
            {
                _logger.LogError(ex, "Invalid Google JWT token");
                return Unauthorized(new
                {
                    success = false,
                    error = "Invalid Google token",
                    details = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google login failed");
                return StatusCode(500, new
                {
                    success = false,
                    error = "Login failed",
                    details = ex.Message
                });
            }
        }

        // ✅ Verify Google JWT Token
        private async Task<GoogleJsonWebSignature.Payload?> VerifyGoogleToken(string credential)
        {
            try
            {
                var clientId = _configuration["Authentication:Google:ClientId"];

                if (string.IsNullOrEmpty(clientId))
                {
                    _logger.LogError("Google ClientId not configured");
                    throw new InvalidOperationException("Google authentication is not properly configured");
                }

                var settings = new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = new[] { clientId }
                };

                var payload = await GoogleJsonWebSignature.ValidateAsync(credential, settings);

                _logger.LogInformation($"Google token verified for: {payload.Email}");

                return payload;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google token validation failed");
                return null;
            }
        }

        // ✅ Generate proper JWT token
        private string GenerateJwtToken(User user)
        {
            var jwtKey = _configuration["Jwt:Key"];
            var jwtIssuer = _configuration["Jwt:Issuer"];
            var jwtAudience = _configuration["Jwt:Audience"];

            if (string.IsNullOrEmpty(jwtKey))
            {
                // Fallback for development (use a secure key in production!)
                jwtKey = "your-super-secret-key-change-this-in-production-min-32-chars";
                _logger.LogWarning("Using default JWT key - configure Jwt:Key in appsettings.json for production!");
            }

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim(JwtRegisteredClaimNames.Name, user.Name),
                new Claim("role", user.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: jwtIssuer ?? "ai-teaching-platform",
                audience: jwtAudience ?? "ai-teaching-platform-users",
                claims: claims,
                expires: DateTime.UtcNow.AddDays(7), // Token valid for 7 days
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new
            {
                message = "Auth endpoint is working!",
                timestamp = DateTime.UtcNow,
                status = "success"
            });
        }

        [HttpGet("verify")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public IActionResult VerifyToken()
        {
            // Check if Authorization header exists
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Unauthorized(new { error = "No token provided" });
            }

            var token = Request.Headers["Authorization"].ToString().Replace("Bearer ", "");

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwtKey = _configuration["Jwt:Key"] ?? "your-super-secret-key-change-this-in-production-min-32-chars";

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ClockSkew = TimeSpan.Zero
                };

                var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);

                return Ok(new
                {
                    valid = true,
                    email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value,
                    name = principal.FindFirst(JwtRegisteredClaimNames.Name)?.Value,
                    role = principal.FindFirst("role")?.Value
                });
            }
            catch
            {
                return Unauthorized(new { valid = false, error = "Invalid token" });
            }
        }
    }

    public class GoogleLoginRequest
    {
        public string Credential { get; set; } = string.Empty;
    }
}
