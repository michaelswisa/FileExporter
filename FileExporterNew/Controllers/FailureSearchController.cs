using Microsoft.AspNetCore.Mvc;
using FileExporterNew.Services;

namespace FileExporterNew.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FailureSearchController : ControllerBase
    {
        private readonly FailureSearchService _failureSearchService;

        public FailureSearchController(FailureSearchService failureSearchService)
        {
            _failureSearchService = failureSearchService;
        }

        [HttpPost("search")]
        public async Task<IActionResult> Search([FromBody] SearchRequest req)
        {
            await _failureSearchService.SearchFolderForFailuresAsync(req.RootDir, req.Path, req.DName, req.Env);
            return Ok(new { status = "done" });
        }

        public class SearchRequest
        {
            public string RootDir { get; set; }
            public string Path { get; set; }
            public string DName { get; set; }
            public string Env { get; set; }
        }
    }
} 