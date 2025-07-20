using FileExporterNew.Models;
using FileExporterNew.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FileExporterNew.Controllers
{
    [ApiController]
    [Route("api/[controller]")] // הנתיב הבסיסי יהיה /api/scan
    public class ScanController : ControllerBase
    {
        private readonly ILogger<ScanController> _logger;
        private readonly FailureSearchService _failureSearcher;
        private readonly ZombieSearchService _zombieSearcher;
        private readonly TranscodedSearchService _transcodedSearcher;
        private readonly Settings _settings;

        // הזרקת כל השירותים הנדרשים דרך הקונסטרקטור
        public ScanController(
            ILogger<ScanController> logger,
            FailureSearchService failureSearcher,
            ZombieSearchService zombieSearcher,
            TranscodedSearchService transcodedSearcher,
            IOptions<Settings> settings)
        {
            _logger = logger;
            _failureSearcher = failureSearcher;
            _zombieSearcher = zombieSearcher;
            _transcodedSearcher = transcodedSearcher;
            _settings = settings.Value;
        }

        [HttpPost("all/{dName}")] // לדוגמה: POST /api/scan/all/Mosh
        public async Task<IActionResult> ScanAll(string dName)
        {
            var (isValid, path, message) = ValidateDName(dName);
            if (!isValid)
            {
                return BadRequest(message);
            }

            _logger.LogInformation("Manual 'All' scan triggered for d_name: {DName}", dName);

            // הפעלת כל הסריקות במקביל
            var tasks = new[]
            {
                _failureSearcher.SearchFolderForFailuresAsync(_settings.RootPath, path, dName, _settings.Env),
                _zombieSearcher.SearchFolderForObservedZombiesAsync(_settings.RootPath, path, dName, _settings.Env),
                _zombieSearcher.SearchFolderForNonObservedZombiesAsync(_settings.RootPath, path, dName, _settings.Env),
                _transcodedSearcher.SearchFoldersForTranscodedAsync(_settings.RootPath, path, dName, _settings.Env)
            };

            await Task.WhenAll(tasks);

            return Ok($"All scans completed for d_name '{dName}'.");
        }

        /// <summary>
        /// Triggers a scan for failures for a specific d_name.
        /// </summary>
        [HttpPost("failures/{dName}")] // לדוגמה: POST /api/scan/failures/Mosh
        public async Task<IActionResult> ScanFailures(string dName)
        {
            var (isValid, path, message) = ValidateDName(dName);
            if (!isValid)
            {
                return BadRequest(message);
            }

            _logger.LogInformation("Manual 'Failures' scan triggered for d_name: {DName}", dName);
            await _failureSearcher.SearchFolderForFailuresAsync(_settings.RootPath, path, dName, _settings.Env);

            return Ok($"Failure scan completed for d_name '{dName}'.");
        }

        /// <summary>
        /// Triggers a scan for observed zombies for a specific d_name.
        /// </summary>
        [HttpPost("zombies/observed/{dName}")] // לדוגמה: POST /api/scan/zombies/observed/Mosh
        public async Task<IActionResult> ScanObservedZombies(string dName)
        {
            var (isValid, path, message) = ValidateDName(dName);
            if (!isValid)
            {
                return BadRequest(message);
            }

            _logger.LogInformation("Manual 'Observed Zombies' scan triggered for d_name: {DName}", dName);
            await _zombieSearcher.SearchFolderForObservedZombiesAsync(_settings.RootPath, path, dName, _settings.Env);

            return Ok($"Observed zombies scan completed for d_name '{dName}'.");
        }

        /// <summary>
        /// Triggers a scan for non-observed zombies for a specific d_name.
        /// </summary>
        [HttpPost("zombies/non-observed/{dName}")] // לדוגמה: POST /api/scan/zombies/non-observed/Mosh
        public async Task<IActionResult> ScanNonObservedZombies(string dName)
        {
            var (isValid, path, message) = ValidateDName(dName);
            if (!isValid)
            {
                return BadRequest(message);
            }

            _logger.LogInformation("Manual 'Non-Observed Zombies' scan triggered for d_name: {DName}", dName);
            await _zombieSearcher.SearchFolderForNonObservedZombiesAsync(_settings.RootPath, path, dName, _settings.Env);

            return Ok($"Non-observed zombies scan completed for d_name '{dName}'.");
        }

        /// <summary>
        /// Triggers a scan for transcoded folders for a specific d_name.
        /// </summary>
        [HttpPost("transcoded/{dName}")] // לדוגמה: POST /api/scan/transcoded/Mosh
        public async Task<IActionResult> ScanTranscoded(string dName)
        {
            var (isValid, path, message) = ValidateDName(dName);
            if (!isValid)
            {
                return BadRequest(message);
            }

            _logger.LogInformation("Manual 'Transcoded' scan triggered for d_name: {DName}", dName);
            await _transcodedSearcher.SearchFoldersForTranscodedAsync(_settings.RootPath, path, dName, _settings.Env);

            return Ok($"Transcoded scan completed for d_name '{dName}'.");
        }


        // פונקציית עזר פרטית לבדיקת תקינות ה-d_name
        private (bool IsValid, string Path, string Message) ValidateDName(string dName)
        {
            if (string.IsNullOrWhiteSpace(dName))
            {
                return (false, null, "dName cannot be empty.");
            }

            var fullPath = Path.Combine(_settings.RootPath, dName);

            if (!Directory.Exists(fullPath))
            {
                _logger.LogWarning("Attempted to scan a non-existent d_name: {DName}", dName);
                return (false, null, $"Directory (d_name) '{dName}' not found in root path '{_settings.RootPath}'.");
            }

            return (true, fullPath, "OK");
        }
    }
}