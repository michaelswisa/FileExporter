using FileExporterNew.Models;
using FileExporterNew.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FileExporterNew.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ZombieSearchController : ControllerBase
    {
        private readonly ZombieSearchService _zombieSearchService;
        private readonly Settings _settings;

        public ZombieSearchController(ZombieSearchService zombieSearchService, IOptions<Settings> settings)
        {
            _zombieSearchService = zombieSearchService;
            _settings = settings.Value;
        }

        [HttpPost("observed")]
        public async Task<IActionResult> SearchFolderForObservedZombiesAsync([FromBody] DNameRequest req)
        {
            var path = Path.Combine(_settings.RootPath, req.DName);
            await _zombieSearchService.SearchFolderForObservedZombiesAsync(_settings.RootPath, path, req.DName, _settings.Env);
            return Ok(new { status = "done" });
        }

        [HttpPost("non-observed")]
        public async Task<IActionResult> SearchFolderForNonObservedZombiesAsync([FromBody] DNameRequest req)
        {
            var path = Path.Combine(_settings.RootPath, req.DName);
            await _zombieSearchService.SearchFolderForNonObservedZombiesAsync(_settings.RootPath, path, req.DName, _settings.Env);
            return Ok(new { status = "done" });
        }
    }

    public class DNameRequest
    {
        public string DName { get; set; } = string.Empty;
    }
}
