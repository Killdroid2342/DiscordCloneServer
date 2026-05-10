using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace DiscordCloneServer.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class UploadController : ControllerBase
    {
        private const int UploadCacheSeconds = 31536000;
        private readonly IWebHostEnvironment _environment;
        private readonly string _cdnBaseUrl;

        public UploadController(IWebHostEnvironment environment, IConfiguration? configuration = null)
        {
            _environment = environment;
            _cdnBaseUrl = (configuration?["Cdn:BaseUrl"] ?? string.Empty).TrimEnd('/');
        }

        [HttpPost("UploadImage")]
        [EnableRateLimiting("upload")]
        public async Task<IActionResult> UploadImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "No file uploaded." });

            const long maxImageBytes = 10 * 1024 * 1024;
            var allowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "image/jpeg",
                "image/png",
                "image/gif",
                "image/webp"
            };

            if (file.Length > maxImageBytes)
            {
                return BadRequest(new { message = "Images must be 10MB or smaller." });
            }

            if (!allowedTypes.Contains(file.ContentType))
            {
                return BadRequest(new { message = "Only JPEG, PNG, GIF, and WEBP images are supported." });
            }

            if (!HasAllowedImageSignature(file))
            {
                return BadRequest(new { message = "Uploaded file contents do not match a supported image type." });
            }

            string webRootPath = _environment.WebRootPath;
            if (string.IsNullOrEmpty(webRootPath))
            {
                webRootPath = Path.Combine(_environment.ContentRootPath, "wwwroot");
            }

            var uploadsFolder = Path.Combine(webRootPath, "uploads");
            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var safeOriginalName = Path.GetFileName(file.FileName);
            var extension = Path.GetExtension(safeOriginalName).ToLowerInvariant();
            var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg",
                ".jpeg",
                ".png",
                ".gif",
                ".webp"
            };

            if (!allowedExtensions.Contains(extension))
            {
                return BadRequest(new { message = "Unsupported image extension." });
            }

            var uniqueFileName = $"{Guid.NewGuid():N}{extension}";
            var filePath = Path.Combine(uploadsFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            var fileUrl = $"/uploads/{uniqueFileName}";
            var cdnUrl = string.IsNullOrWhiteSpace(_cdnBaseUrl)
                ? fileUrl
                : $"{_cdnBaseUrl}{fileUrl}";
            return Ok(new
            {
                url = fileUrl,
                cdnUrl,
                cacheControl = $"public,max-age={UploadCacheSeconds},immutable"
            });
        }

        private static bool HasAllowedImageSignature(IFormFile file)
        {
            Span<byte> header = stackalloc byte[12];
            using var stream = file.OpenReadStream();
            var read = stream.Read(header);
            if (read < 4)
            {
                return false;
            }

            var bytes = header[..read];
            var isJpeg = bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            var isPng = bytes.Length >= 8 &&
                        bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                        bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A;
            var isGif = bytes.Length >= 6 &&
                        bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 &&
                        bytes[3] == 0x38 && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61;
            var isWebp = bytes.Length >= 12 &&
                         bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                         bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50;

            return isJpeg || isPng || isGif || isWebp;
        }
    }
}
