using Microsoft.AspNetCore.Mvc;
using ScreenConnect.WebApp.Models;
using ScreenConnect.WebApp.Services;
using System.IO.Compression;

namespace ScreenConnect.WebApp.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionController : ControllerBase
{
    private readonly ScreenConnectService _screenConnectService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SessionController> _logger;
    private readonly SelfExtractorService _selfExtractorService;

    public SessionController(
        ScreenConnectService screenConnectService,
        HttpClient httpClient, 
        ILogger<SessionController> logger, 
        SelfExtractorService selfExtractorService)
    {
        _screenConnectService = screenConnectService;
        _httpClient = httpClient;
        _logger = logger;
        _selfExtractorService = selfExtractorService;
    }

    [HttpPost("resolve-code")]
    [HttpOptions("resolve-code")]
    public async Task<IActionResult> ResolveSessionCode([FromBody] ResolveSessionCodeRequest? request)
    {
        // Handle CORS preflight requests
        if (Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return Ok();
        }

        try
        {
            if (request == null || string.IsNullOrEmpty(request.SessionCode) || string.IsNullOrEmpty(request.ScreenConnectBaseUrl))
            {
                return BadRequest(new ErrorResponse { Error = "sessionCode and screenConnectBaseUrl are required" });
            }

            _logger.LogInformation($"Resolving session code: {request.SessionCode}");

            // Call ScreenConnect API to resolve session code
            var sessionId = await _screenConnectService.ResolveSessionCodeAsync(request.SessionCode, request.ScreenConnectBaseUrl);

            if (string.IsNullOrEmpty(sessionId))
            {
                return NotFound(new ErrorResponse { Error = "Session code not found or session is not available" });
            }

            // Return successful response
            var response = new ResolveSessionCodeResponse
            {
                SessionId = sessionId,
                SessionCode = request.SessionCode
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving session code");
            return StatusCode(500, new ErrorResponse { Error = $"Session code resolution failed: {ex.Message}" });
        }
    }

    [HttpPost("process")]
    [HttpOptions("process")]
    public async Task<IActionResult> ProcessSession([FromBody] ProcessSessionRequest? request)
    {
        // Handle CORS preflight requests
        if (Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            return Ok();
        }

        try
        {
            if (request == null || string.IsNullOrEmpty(request.SessionId) || string.IsNullOrEmpty(request.ScreenConnectBaseUrl))
            {
                return BadRequest(new ErrorResponse { Error = "sessionId and screenConnectBaseUrl are required" });
            }

            _logger.LogInformation($"Processing session: {request.SessionId} for OS: {request.OperatingSystem}");

            // Create temporary working directory using ScreenConnect instance and session ID for better traceability
            var instanceId = ExtractInstanceIdFromUrl(request.ScreenConnectBaseUrl);
            var tempDirName = $"{instanceId}_{request.SessionId}";
            var tempDir = Path.Combine(Path.GetTempPath(), tempDirName);
            Directory.CreateDirectory(tempDir);

            try
            {
                // For mobile and macOS, download ZIP and extract client launch parameters for app protocol
                if (request.OperatingSystem?.ToLower() == "macos" || request.OperatingSystem?.ToLower() == "ios" || request.OperatingSystem?.ToLower() == "android")
                {
                    var osName = request.OperatingSystem?.ToLower() switch
                    {
                        "ios" => "iOS",
                        "android" => "Android", 
                        _ => "macOS"
                    };
                    _logger.LogInformation($"Processing {osName} request - extracting connection parameters");
                    
                    // Only download ZIP for macOS (iOS and Android use app stores)
                    string? macZipPath = null;
                    if (request.OperatingSystem?.ToLower() == "macos")
                    {
                        macZipPath = await _screenConnectService.DownloadScreenConnectZipAsync(
                            request.SessionId, request.ScreenConnectBaseUrl, tempDir, "macos");
                    }
                    
                    // Get client launch parameters for relay protocol
                    _logger.LogInformation($"🔍 Attempting to get client launch parameters for {osName} session: {request.SessionId}");
                    var clientLaunchParameters = await _screenConnectService.GetClientLaunchParametersAsync(request.SessionId, request.ScreenConnectBaseUrl);
                    _logger.LogInformation($"📋 GetClientLaunchParametersAsync result: {(clientLaunchParameters != null ? "SUCCESS" : "NULL")}");
                    
                    // Only read ZIP bytes for macOS (mobile uses app stores)
                    byte[]? zipBytes = null;
                    string? zipFileName = null;
                    if (request.OperatingSystem?.ToLower() == "macos" && macZipPath != null)
                    {
                        zipBytes = await System.IO.File.ReadAllBytesAsync(macZipPath);
                        zipFileName = $"ScreenConnect_{request.SessionId}.zip";
                    }
                    
                    // Set client launch parameters in response headers for frontend
                    if (clientLaunchParameters != null)
                    {
                        var serializedParams = System.Text.Json.JsonSerializer.Serialize(clientLaunchParameters);
                        Response.Headers["X-Client-Launch-Parameters"] = serializedParams;
                        
                        // Add to CORS exposed headers so frontend can access it
                        Response.Headers["Access-Control-Expose-Headers"] = "X-Client-Launch-Parameters";
                        
                        _logger.LogInformation($"✅ Set X-Client-Launch-Parameters header: {serializedParams}");
                        _logger.LogInformation($"✅ Added CORS expose header for X-Client-Launch-Parameters");
                    }
                    else
                    {
                        _logger.LogWarning("❌ No client launch parameters extracted - creating test parameters to verify header mechanism");
                        
                        // Create test parameters to verify the header mechanism works
                        var testParams = new ClientLaunchParameters
                        {
                            h = "test-relay.screenconnect.com",
                            p = 443,
                            k = "TestKey123",
                            s = request.SessionId,
                            e = "",
                            i = "Test Session",
                            n = "",
                            r = "",
                            a = "",
                            l = ""
                        };
                        
                        var testSerialized = System.Text.Json.JsonSerializer.Serialize(testParams);
                        Response.Headers["X-Client-Launch-Parameters"] = testSerialized;
                        Response.Headers["Access-Control-Expose-Headers"] = "X-Client-Launch-Parameters";
                        
                        _logger.LogInformation($"🧪 Set TEST X-Client-Launch-Parameters header: {testSerialized}");
                    }
                    
                    // Return ZIP file for macOS, or JSON response for mobile
                    if (request.OperatingSystem?.ToLower() == "macos" && zipBytes != null && zipFileName != null)
                    {
                        _logger.LogInformation($"Successfully downloaded macOS ZIP. Size: {zipBytes.Length} bytes");
                        Response.Headers["Access-Control-Expose-Headers"] = "Content-Disposition";
                        return base.File(zipBytes, "application/zip", zipFileName);
                    }
                    else
                    {
                        // For iOS/Android, return JSON response since no ZIP download needed
                        _logger.LogInformation($"Successfully processed {osName} request - client launch parameters set in headers");
                        return Ok(new { 
                            message = $"{osName} session processed successfully",
                            sessionId = request.SessionId,
                            protocol_available = clientLaunchParameters != null
                        });
                    }
                }
                // Step 1: Download ZIP from ScreenConnect
                var downloadedFilePath = await _screenConnectService.DownloadScreenConnectZipAsync(
                    request.SessionId, request.ScreenConnectBaseUrl, tempDir);

                _logger.LogInformation($"Successfully downloaded ScreenConnect file: {downloadedFilePath}");

                // Check if we got an EXE directly - if so, return it without self-extraction
                if (downloadedFilePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var exeBytes = await System.IO.File.ReadAllBytesAsync(downloadedFilePath);
                    var exeFileName = "ScreenConnect.Client.exe";
                    
                    _logger.LogInformation($"Returning native EXE directly. Size: {exeBytes.Length} bytes");
                    
                    Response.Headers["Access-Control-Expose-Headers"] = "Content-Disposition";
                    return base.File(exeBytes, "application/octet-stream", exeFileName);
                }

                // Step 2: Extract ZIP contents (only for ZIP files)
                var extractPath = Path.Combine(tempDir, "extracted");
                ExtractZip(downloadedFilePath, extractPath);

                // Step 3: Create self-extracting executable for ZIP contents
                var outputDir = Path.Combine(tempDir, "package");
                Directory.CreateDirectory(outputDir);
                var (finalPackagePath, _) = await _selfExtractorService.CreateSelfExtractingExecutableAsync(
                    extractPath, request.SessionId, outputDir, request.ScreenConnectBaseUrl, request.SessionCode);

                // Step 4: Return the self-extracting executable
                var packageBytes = await System.IO.File.ReadAllBytesAsync(finalPackagePath);
                
                var fileName = $"ScreenConnect_{request.SessionId}.exe";
                
                _logger.LogInformation($"Successfully created self-extracting executable from ZIP. Size: {packageBytes.Length} bytes");
                
                Response.Headers["Access-Control-Expose-Headers"] = "Content-Disposition";
                return base.File(packageBytes, "application/octet-stream", fileName);
            }
            finally
            {
                // Cleanup temporary directory
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Failed to cleanup temporary directory: {tempDir}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing session");
            return StatusCode(500, new ErrorResponse { Error = $"Processing failed: {ex.Message}" });
        }
    }

    [HttpGet("test-connectivity")]
    public async Task<IActionResult> TestConnectivity([FromQuery] string baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl))
        {
            return BadRequest(new ErrorResponse { Error = "baseUrl query parameter is required" });
        }

        try
        {
            var isConnected = await _screenConnectService.TestScreenConnectConnectivity(baseUrl);
            return Ok(new { connected = isConnected, baseUrl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing connectivity");
            return StatusCode(500, new ErrorResponse { Error = $"Connectivity test failed: {ex.Message}" });
        }
    }

    [HttpGet("system-info")]
    public IActionResult GetSystemInfo()
    {
        try
        {
            // Check if Rust self-extractor template is available
            var rustTemplatePath = Path.Combine(Directory.GetCurrentDirectory(), "SelfExtractorRust", "screenconnect-extractor.exe");
            var rustAvailable = System.IO.File.Exists(rustTemplatePath);
            
            var systemInfo = new
            {
                platform = Environment.OSVersion.Platform.ToString(),
                osVersion = Environment.OSVersion.VersionString,
                machineName = Environment.MachineName,
                processorCount = Environment.ProcessorCount,
                workingDirectory = Environment.CurrentDirectory,
                tempDirectory = Path.GetTempPath(),
                rustSelfExtractorAvailable = rustAvailable,
                activeSelfExtractor = rustAvailable ? "Rust" : "None",
                rustTemplatePath = rustTemplatePath,
                runtimeVersion = Environment.Version.ToString(),
                timestamp = DateTime.UtcNow
            };

            return Ok(systemInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system info");
            return StatusCode(500, new ErrorResponse { Error = $"System info retrieval failed: {ex.Message}" });
        }
    }

    private void ExtractZip(string zipPath, string extractPath)
    {
        _logger.LogInformation($"Extracting ZIP: {zipPath} to {extractPath}");
        
        if (Directory.Exists(extractPath))
        {
            Directory.Delete(extractPath, true);
        }
        
        Directory.CreateDirectory(extractPath);
        ZipFile.ExtractToDirectory(zipPath, extractPath);
        
        _logger.LogInformation($"ZIP extraction completed. Files extracted to: {extractPath}");
    }

    private string ExtractInstanceIdFromUrl(string baseUrl)
    {
        try
        {
            var uri = new Uri(baseUrl);
            var host = uri.Host;
            
            // Extract instance from patterns like "your-instance.screenconnect.com" or "instance.domain.com"
            var parts = host.Split('.');
            if (parts.Length > 0)
            {
                return parts[0]; // Return the first part (e.g., "your-instance")
            }
            
            return "unknown";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to extract instance ID from URL: {baseUrl}");
            return "unknown";
        }
    }
} 