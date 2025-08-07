using FileExporterGinari;
using Microsoft.AspNetCore.Mvc;

namespace FileExporterNew.Controllers
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

        /// <summary>
        /// Triggers all scan types for a specific dName to run in the background.
        /// </summary>
        [HttpPost("all/{dName}")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        public IActionResult TriggerAllScansForDName(string dName)
        {
            _logger.LogInformation("API request to trigger all scans for dName: {DName}", dName);
            // Don't await, run in the background
            _ = _scanManager.ScanAllTypesForDNameAsync(dName)
                .ContinueWith(t => {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "Background scan for all types for dName {dName} failed.", dName);
                    }
                });

            return Accepted($"All scans for dName '{dName}' have been queued for execution.");
        }

        /// <summary>
        /// Triggers only the failure scan for a specific dName to run in the background.
        /// </summary>
        [HttpPost("failures/{dName}")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        public IActionResult TriggerFailureScan(string dName)
        {
            _logger.LogInformation("API request to trigger failure scan for dName: {DName}", dName);
            // Don't await, run in the background
            _ = _scanManager.ScanFailuresForDNameAsync(dName)
                .ContinueWith(t => {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "Background failure scan for dName {dName} failed.", dName);
                    }
                });

            return Accepted($"Failure scan for dName '{dName}' has been queued for execution.");
        }

        /// <summary>
        /// Triggers only the observed zombies scan for a specific dName to run in the background.
        /// </summary>
        [HttpPost("zombies/observed/{dName}")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        public IActionResult TriggerObservedZombieScan(string dName)
        {
            _logger.LogInformation("API request to trigger observed zombie scan for dName: {DName}", dName);
            // Don't await, run in the background
            _ = _scanManager.ScanZombiesForDNameAsync(dName, "observed")
                .ContinueWith(t => {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "Background observed zombie scan for dName {dName} failed.", dName);
                    }
                });

            return Accepted($"Observed zombie scan for dName '{dName}' has been queued for execution.");
        }

        /// <summary>
        /// Triggers only the non-observed zombies scan for a specific dName to run in the background.
        /// </summary>
        [HttpPost("zombies/non-observed/{dName}")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        public IActionResult TriggerNonObservedZombieScan(string dName)
        {
            _logger.LogInformation("API request to trigger non-observed zombie scan for dName: {DName}", dName);
            // Don't await, run in the background
            _ = _scanManager.ScanZombiesForDNameAsync(dName, "non-observed")
                .ContinueWith(t => {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "Background non-observed zombie scan for dName {dName} failed.", dName);
                    }
                });

            return Accepted($"Non-observed zombie scan for dName '{dName}' has been queued for execution.");
        }

        /// <summary>
        /// Triggers only the transcoded scan for a specific dName to run in the background.
        /// </summary>
        [HttpPost("transcoded/{dName}")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        public IActionResult TriggerTranscodedScan(string dName)
        {
            _logger.LogInformation("API request to trigger transcoded scan for dName: {DName}", dName);
            // Don't await, run in the background
            _ = _scanManager.ScanTranscodedForDNameAsync(dName)
                .ContinueWith(t => {
                    if (t.IsFaulted)
                    {
                        _logger.LogError(t.Exception, "Background transcoded scan for dName {dName} failed.", dName);
                    }
                });

            return Accepted($"Transcoded scan for dName '{dName}' has been queued for execution.");
        }
    }
}