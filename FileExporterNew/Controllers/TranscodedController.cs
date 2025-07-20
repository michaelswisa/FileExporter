using FileExporterNew.Models;
using FileExporterNew.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FileExporterNew.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TranscodedController : ControllerBase
    {
        private readonly ILogger<TranscodedController> _logger;
        private readonly TranscodedSearchService _transcodedSearchService;
        private readonly Settings _settings;

        public TranscodedController(ILogger<TranscodedController> logger, TranscodedSearchService transcodedSearchService, IOptions<Settings> settings)
        {
            _logger = logger;
            _transcodedSearchService = transcodedSearchService;
            _settings = settings.Value;
        }

        [HttpPost("scan")]
        public async Task<IActionResult> StartScanAsync([FromBody] ScanRequest request)
        {
            _logger.LogInformation("Received request to start transcoded scan for dName: {DName} in path: {Path}", request.DName, request.Path);

            try
            {
                await _transcodedSearchService.SearchFoldersForTranscodedAsync(
                    _settings.RootPath,
                    request.Path,
                    request.DName,
                    _settings.Env
                );

                return Ok(new { Message = $"Scan for dName '{request.DName}' initiated successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while executing the transcoded scan for dName: {DName}", request.DName);
                return StatusCode(500, "An internal server error occurred. Please check the logs for details.");
            }
        }
    }

    public class ScanRequest
    {
        public string DName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }
}