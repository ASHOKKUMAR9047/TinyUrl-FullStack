using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TinyUrl.Api.Data;
using TinyUrl.Api.Models;

namespace TinyUrl.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableCors("AllowFrontend")]
    public class UrlController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ILogger<UrlController> _logger;
        private readonly IConfiguration _configuration;
        private static readonly Random _random = new Random();
        private const string AlphanumericChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        public UrlController(AppDbContext context, ILogger<UrlController> logger, IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
        }

        // GET: api/public
        // Exposes list of public shortened URLs, ordered by newest first, supporting optional search keyword
        [HttpGet("/api/public")]
        public async Task<IActionResult> GetPublicUrls([FromQuery] string? search = null)
        {
            _logger.LogInformation("HTTP GET /api/public: Fetching public shortened URLs.");
            
            var query = _context.ShortUrls
                .Where(u => !u.IsPrivate);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(u => u.OriginalURL.ToLower().Contains(lowerSearch) || 
                                         u.Code.ToLower().Contains(lowerSearch) ||
                                         u.ShortURL.ToLower().Contains(lowerSearch));
            }

            var urls = await query
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();

            return Ok(urls);
        }

        // POST: api/add
        // Submits an original URL to shorten, returning a created ShortUrl object matching TinyUrl schema
        [HttpPost("/api/add")]
        public async Task<IActionResult> ShortenUrl([FromBody] TinyUrlAddDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.OriginalURL))
            {
                return BadRequest(new { error = "Original URL is required." });
            }

            // Client URL format validation
            if (!Uri.TryCreate(request.OriginalURL, UriKind.Absolute, out var validatedUri) || 
                (validatedUri.Scheme != Uri.UriSchemeHttp && validatedUri.Scheme != Uri.UriSchemeHttps))
            {
                return BadRequest(new { error = "A valid absolute HTTP/HTTPS URL is required." });
            }

            // 1. Smart Loop Prevention (Only block actual redirect code loops like shortener-domain/xxxxxx)
            string currentHost = Request.Host.Host;
            bool isShortenerHost = validatedUri.Host.Equals(currentHost, StringComparison.OrdinalIgnoreCase) || 
                                   validatedUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                                   validatedUri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);

            string path = validatedUri.AbsolutePath.Trim('/');
            bool isShortCodePath = System.Text.RegularExpressions.Regex.IsMatch(path, "^[a-zA-Z0-9]{6}$");

            if (isShortenerHost && isShortCodePath)
            {
                return BadRequest(new { error = "Loop prevention: You cannot shorten an existing short redirection link." });
            }

            // 2. Active Website Verification (Check if the page exists and returns a successful response)
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(5); // 5-second timeout
                    httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko)");

                    // Try a fast HEAD request first
                    var requestMessage = new HttpRequestMessage(HttpMethod.Head, request.OriginalURL);
                    var response = await httpClient.SendAsync(requestMessage);

                    if (!response.IsSuccessStatusCode)
                    {
                        // Fallback to GET in case the target server blocks HEAD requests
                        var getResponse = await httpClient.GetAsync(request.OriginalURL);
                        if (!getResponse.IsSuccessStatusCode)
                        {
                            return BadRequest(new { error = $"The website address you entered is invalid or returned a {(int)getResponse.StatusCode} (Not Found) error." });
                        }
                    }
                }
            }
            catch (Exception)
            {
                return BadRequest(new { error = "Website check failed: The destination website address is offline or unreachable." });
            }

            try
            {
                // Generate a unique 6-character alphanumeric short code
                string code = await GenerateUniqueShortCodeAsync();

                // Construct fully-qualified short URL based on active request headers
                string host = Request.Host.Value ?? "localhost";
                string scheme = Request.Scheme ?? "http";
                string shortURL = $"{scheme}://{host}/{code}";

                var newUrl = new ShortUrl
                {
                    Code = code,
                    OriginalURL = request.OriginalURL,
                    ShortURL = shortURL,
                    TotalClicks = 0,
                    IsPrivate = request.IsPrivate,
                    CreatedAt = DateTime.UtcNow
                };

                _context.ShortUrls.Add(newUrl);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Successfully shortened URL: {Original} to {Code} (Private: {IsPrivate})", 
                    request.OriginalURL, code, request.IsPrivate);

                return CreatedAtAction(nameof(RedirectToOriginal), new { code = newUrl.Code }, newUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while shortening URL: {OriginalURL}", request.OriginalURL);
                return StatusCode(500, new { error = "An internal server error occurred." });
            }
        }

        // GET: /{code} (Mapped at the root directory to match live routing)
        // Redirects short code path to original destination and increments redirect click statistics
        [HttpGet("/{code:regex(^[[a-zA-Z0-9]]{{6}}$)}")]
        public async Task<IActionResult> RedirectToOriginal(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length != 6)
            {
                return BadRequest(new { error = "Invalid short code format." });
            }

            var entry = await _context.ShortUrls.FirstOrDefaultAsync(u => u.Code == code);
            if (entry == null)
            {
                _logger.LogWarning("Redirection failed. Short code not found: {Code}", code);
                return NotFound(new { error = "Short URL not found." });
            }

            try
            {
                // Increment click counter
                entry.TotalClicks++;
                _context.ShortUrls.Update(entry);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Short code {Code} accessed. Redirecting to {OriginalURL}. Clicks: {TotalClicks}", 
                    code, entry.OriginalURL, entry.TotalClicks);

                return Redirect(entry.OriginalURL);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing redirect for short code {Code}", code);
                // Redirect anyway as fallback
                return Redirect(entry.OriginalURL);
            }
        }

        // PUT: /api/update/{code}
        // Increments click count manually for a short code
        [HttpPut("/api/update/{code}")]
        public async Task<IActionResult> UpdateClicks(string code)
        {
            var entry = await _context.ShortUrls.FirstOrDefaultAsync(u => u.Code == code);
            if (entry == null)
            {
                return NotFound(new { error = "Short URL not found." });
            }

            try
            {
                entry.TotalClicks++;
                _context.ShortUrls.Update(entry);
                await _context.SaveChangesAsync();
                return Ok(entry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while manually updating clicks for code {Code}", code);
                return StatusCode(500);
            }
        }

        // DELETE: /api/delete/{code}
        // Deletes a shortened URL entry by its code
        [HttpDelete("/api/delete/{code}")]
        public async Task<IActionResult> DeleteUrl(string code)
        {
            var entry = await _context.ShortUrls.FirstOrDefaultAsync(u => u.Code == code);
            if (entry == null)
            {
                _logger.LogWarning("Deletion failed. Code not found: {Code}", code);
                return NotFound(new { error = "Short URL not found." });
            }

            try
            {
                _context.ShortUrls.Remove(entry);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Successfully deleted short URL: {Code}", code);
                return Ok(new { message = "Short URL successfully deleted." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while deleting short URL code {Code}", code);
                return StatusCode(500, new { error = "An internal server error occurred." });
            }
        }

        // DELETE: /api/delete-all
        // Clears the entire database (requires SecretToken validation query parameter for security)
        [HttpDelete("/api/delete-all")]
        public async Task<IActionResult> DeleteAll([FromQuery] string? secretToken = null)
        {
            // Retrieve configured secret token (falls back to secure default if unset)
            string expectedToken = _configuration["SecretToken"] ?? "AdminSecretToken";

            if (string.IsNullOrEmpty(secretToken) || secretToken != expectedToken)
            {
                _logger.LogWarning("Unauthorized attempt to delete all database entries.");
                return Unauthorized(new { error = "Invalid or missing secretToken." });
            }

            _logger.LogInformation("HTTP DELETE: Bulk Database Cleanup triggered.");
            try
            {
                int countBefore = await _context.ShortUrls.CountAsync();
                
                if (countBefore > 0)
                {
                    await _context.ShortUrls.ExecuteDeleteAsync();
                    _logger.LogInformation("Database cleared successfully. Deleted {Count} short URL mappings.", countBefore);
                    return Ok(new { message = "Database cleared successfully.", deletedCount = countBefore });
                }
                else
                {
                    _logger.LogInformation("Bulk cleanup triggered but database is already empty.");
                    return Ok(new { message = "Database is already empty. No deletion needed.", deletedCount = 0 });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred during bulk database cleanup execution.");
                return StatusCode(500, new { error = "An error occurred while executing bulk database cleanup." });
            }
        }

        #region Helpers

        private async Task<string> GenerateUniqueShortCodeAsync()
        {
            while (true)
            {
                var code = GenerateShortCode();
                var exists = await _context.ShortUrls.AnyAsync(u => u.Code == code);
                if (!exists)
                {
                    return code;
                }
            }
        }

        private string GenerateShortCode()
        {
            var code = new char[6];
            for (int i = 0; i < 6; i++)
            {
                code[i] = AlphanumericChars[_random.Next(AlphanumericChars.Length)];
            }
            return new string(code);
        }

        #endregion
    }

    public class TinyUrlAddDto
    {
        [JsonPropertyName("originalURL")]
        public string OriginalURL { get; set; } = string.Empty;

        [JsonPropertyName("isPrivate")]
        public bool IsPrivate { get; set; } = false;
    }
}
