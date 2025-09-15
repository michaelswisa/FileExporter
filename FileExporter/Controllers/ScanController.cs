using FileExporter.Models;
using FileExporter.Services;
using Microsoft.AspNetCore.Mvc;

namespace FileExporter.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ScanController : ControllerBase
    {
        private readonly ILogger<ScanController> _logger;
        private readonly ScanManagerService _scanManager;

        public ScanController(ILogger<ScanController> logger, ScanManagerService scanManager)
        {
            _logger = logger;
            _scanManager = scanManager;
        }

        [HttpPost("all/{dName}")]
        [ProducesResponseType(typeof(ScanAllResult), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> TriggerAllScansForDName(string dName)
        {
            _logger.LogInformation("API request to trigger all scans for dName: {DName}", dName);

            // שינוי: במקום לקרוא למתודה אחת, אנו קוראים לכל מתודת Queue בנפרד
            // כדי לבנות את התשובה המפורטת, מבלי להמתין לסיום הסריקות.
            var result = new ScanAllResult
            {
                FailureScanQueued = await _scanManager.QueueFailureScanForDNameAsync(dName),
                ObservedZombieScanQueued = await _scanManager.QueueZombiesForDNameAsync(dName, ZombieType.Observed),
                NonObservedZombieScanQueued = await _scanManager.QueueZombiesForDNameAsync(dName, ZombieType.Non_Observed),
                TranscodedScanQueued = await _scanManager.QueueTranscodedScanForDNameAsync(dName)
            };

            // הוספת הודעות למשתמש בהתבסס על מה שהצליח להיכנס לתור
            result.Messages.Add($"Failure scan: {(result.FailureScanQueued ? "Queued" : "Skipped (directory not found)")}.");
            result.Messages.Add($"Observed Zombie scan: {(result.ObservedZombieScanQueued ? "Queued" : "Skipped (directory not found)")}.");
            result.Messages.Add($"Non-Observed Zombie scan: {(result.NonObservedZombieScanQueued ? "Queued" : "Skipped (directory not found)")}.");
            result.Messages.Add($"Transcoded scan: {(result.TranscodedScanQueued ? "Queued" : "Skipped (directory not found)")}.");

            if (!result.AnyScanQueued)
            {
                return NotFound($"No matching directories found to scan for dName '{dName}'.");
            }

            return Accepted(result);
        }

        [HttpPost("failures/{dName}")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> TriggerFailureScan(string dName)
        {
            _logger.LogInformation("API request to trigger failure scan for dName: {DName}", dName);

            // שינוי: קריאה למתודת ה-Queue החדשה
            var scanQueued = await _scanManager.QueueFailureScanForDNameAsync(dName);

            if (scanQueued)
            {
                return Accepted($"Failure scan for dName '{dName}' has been queued for execution.");
            }
            else
            {
                return NotFound($"Directory for failure scan corresponding to dName '{dName}' not found.");
            }
        }

        [HttpPost("zombies/observed/{dName}")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> TriggerObservedZombieScan(string dName)
        {
            _logger.LogInformation("API request to trigger observed zombie scan for dName: {DName}", dName);

            // שינוי: קריאה למתודת ה-Queue החדשה
            var scanQueued = await _scanManager.QueueZombiesForDNameAsync(dName, ZombieType.Observed);

            if (scanQueued)
            {
                return Accepted($"Observed zombie scan for dName '{dName}' has been queued for execution.");
            }
            else
            {
                return NotFound($"Directory for observed zombie scan corresponding to dName '{dName}' not found.");
            }
        }

        [HttpPost("zombies/non-observed/{dName}")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> TriggerNonObservedZombieScan(string dName)
        {
            _logger.LogInformation("API request to trigger non-observed zombie scan for dName: {DName}", dName);

            // שינוי: קריאה למתודת ה-Queue החדשה
            var scanQueued = await _scanManager.QueueZombiesForDNameAsync(dName, ZombieType.Non_Observed);

            if (scanQueued)
            {
                return Accepted($"Non-observed zombie scan for dName '{dName}' has been queued for execution.");
            }
            else
            {
                return NotFound($"Directory for non-observed zombie scan corresponding to dName '{dName}' not found.");
            }
        }


        [HttpPost("transcoded/{dName}")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> TriggerTranscodedScan(string dName)
        {
            _logger.LogInformation("API request to trigger transcoded scan for dName: {DName}", dName);

            // שינוי: קריאה למתודת ה-Queue החדשה
            var scanQueued = await _scanManager.QueueTranscodedScanForDNameAsync(dName);

            if (scanQueued)
            {
                return Accepted($"Transcoded scan for dName '{dName}' has been queued for execution.");
            }
            else
            {
                return NotFound($"Directory for transcoded scan corresponding to dName '{dName}' not found.");
            }
        }
    }
}