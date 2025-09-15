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

            var result = await _scanManager.ScanAllTypesForDNameAsync(dName);

            if (!result.AnyScanQueued)
            {
                return NotFound($"No matching directories found to scan for dName '{dName}'.");
            }

            // The result object will be serialized in the response body
            return Accepted(result);
        }

        [HttpPost("failures/{dName}")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> TriggerFailureScan(string dName)
        {
            _logger.LogInformation("API request to trigger failure scan for dName: {DName}", dName);

            var scanQueued = await _scanManager.ScanFailuresForDNameAsync(dName);

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

            var scanQueued = await _scanManager.ScanZombiesForDNameAsync(dName, ZombieType.Observed);

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

            var scanQueued = await _scanManager.ScanZombiesForDNameAsync(dName, ZombieType.Non_Observed);

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

            var scanQueued = await _scanManager.ScanTranscodedForDNameAsync(dName);

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