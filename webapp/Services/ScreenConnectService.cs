using System.Net;
using System.Text;
using System.Web;
using Newtonsoft.Json;
using ScreenConnect.WebApp.Models;

namespace ScreenConnect.WebApp.Services;

public class ScreenConnectService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ScreenConnectService> _logger;

    public ScreenConnectService(IHttpClientFactory httpClientFactory, ILogger<ScreenConnectService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string?> ResolveSessionCodeAsync(string sessionCode, string baseUrl)
    {
        try
        {
            // Use the working endpoint from HAR file analysis
            var endpoint = "/Services/PageService.ashx/GetLiveData";
            var result = await TryResolveWithEndpoint(sessionCode, baseUrl, endpoint);
            
            if (result != null)
            {
                return result;
            }

            _logger.LogError("API endpoint failed - this suggests authentication or session access issues");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Unexpected error in ResolveSessionCodeAsync: {ex.Message}");
            throw;
        }
    }

    private async Task<string?> TryResolveWithEndpoint(string sessionCode, string baseUrl, string endpoint)
    {
        var url = $"{baseUrl.TrimEnd('/')}{endpoint}";
        
        // Use the correct format as shown in the HAR file
        var payload = new object[]
        {
            new 
            {
                GuestSessionInfo = new 
                {
                    sessionCodes = new[] { sessionCode },  // camelCase as per HAR file
                    sessionIDs = new string[0]             // camelCase as per HAR file
                }
            },
            0  // Version number as shown in HAR file
        };

        _logger.LogInformation($"Trying endpoint: {url}");
        _logger.LogInformation($"Request payload: {JsonConvert.SerializeObject(payload, Formatting.Indented)}");

        var json = JsonConvert.SerializeObject(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var httpClient = _httpClientFactory.CreateClient();
        
        // Add timeout to prevent hanging requests
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        
        // Add common headers that ScreenConnect expects based on HAR file
        httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");
        httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
        
        _logger.LogInformation($"Sending POST request to: {url}");
        _logger.LogInformation($"Content-Type: application/json");
        _logger.LogInformation($"Request body: {json}");
        
        var response = await httpClient.PostAsync(url, content);

        // Log response details before checking success
        _logger.LogInformation($"Response status: {response.StatusCode} ({(int)response.StatusCode})");
        _logger.LogInformation($"Response headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}");

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError($"ScreenConnect API returned status {response.StatusCode}: {errorContent}");
            _logger.LogError($"Request URL: {url}");
            _logger.LogError($"Request payload: {json}");
            
            // For 401/403, suggest authentication issues
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            {
                throw new Exception($"ScreenConnect API authentication error: HTTP {response.StatusCode}. May require x-anti-forgery-token or authentication cookies.");
            }
            
            // For 404, this endpoint might not exist on this version
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new Exception($"ScreenConnect API endpoint not found: {endpoint}. This version might use a different API.");
            }
            
            throw new Exception($"ScreenConnect API error: HTTP {response.StatusCode} - {errorContent}");
        }

        // Parse the response
        var responseContent = await response.Content.ReadAsStringAsync();
        _logger.LogInformation($"ScreenConnect API response: {responseContent}");

        // Try to parse as the expected format first
        try
        {
            var apiResponse = JsonConvert.DeserializeObject<ScreenConnectApiResponse>(responseContent);

            if (apiResponse?.ResponseInfoMap?.GuestSessionInfo?.Sessions != null)
            {
                // Find the session with matching code
                foreach (var session in apiResponse.ResponseInfoMap.GuestSessionInfo.Sessions)
                {
                    if (session.Code == sessionCode && !string.IsNullOrEmpty(session.SessionID))
                    {
                        _logger.LogInformation($"Found session ID {session.SessionID} for code {sessionCode}");
                        return session.SessionID;
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning($"Failed to parse response as expected format: {ex.Message}");
            _logger.LogInformation($"Raw response: {responseContent}");
            
            // Try alternative parsing approaches
            try
            {
                // Try parsing as a simple session object or array
                if (responseContent.StartsWith("["))
                {
                    var sessions = JsonConvert.DeserializeObject<SessionInfo[]>(responseContent);
                    if (sessions != null)
                    {
                        var matchingSession = sessions.FirstOrDefault(s => s.Code == sessionCode);
                        if (matchingSession != null && !string.IsNullOrEmpty(matchingSession.SessionID))
                        {
                            _logger.LogInformation($"Found session ID {matchingSession.SessionID} for code {sessionCode} (alternative format)");
                            return matchingSession.SessionID;
                        }
                    }
                }
            }
            catch (Exception altEx)
            {
                _logger.LogWarning($"Alternative parsing also failed: {altEx.Message}");
            }
        }

        _logger.LogWarning($"No session found for code {sessionCode} in endpoint {endpoint}");
        return null;
    }

    public async Task<string> DownloadScreenConnectZipAsync(string sessionId, string baseUrl, string tempDir)
    {
        return await DownloadScreenConnectZipAsync(sessionId, baseUrl, tempDir, "windows");
    }

    public async Task<string> DownloadScreenConnectZipAsync(string sessionId, string baseUrl, string tempDir, string operatingSystem)
    {
        string[] urlPatterns;
        
        if (operatingSystem?.ToLower() == "macos")
        {
            // macOS uses MacBundleDownload from HAR file analysis
            // The URL will contain client launch parameters (h, p, k, etc.) that we need for relay protocol
            urlPatterns = new[]
            {
                // Primary macOS pattern from HAR file - this will include dynamic parameters
                $"{baseUrl}/Bin/ScreenConnect.Client.zip",
                // With session parameters (from HAR file format)
                $"{baseUrl}/Bin/ScreenConnect.Client.zip?s={sessionId}",
                $"{baseUrl}/Bin/ScreenConnect.Client.zip?c={sessionId}",
            };
        }
        else
        {
            // Windows patterns (prioritizing ScreenConnect.Client.exe for upgraded instances)
            urlPatterns = new[]
            {
                // Primary EXE pattern for upgraded ScreenConnect instances
                $"{baseUrl}/Bin/ScreenConnect.Client.exe",
                $"{baseUrl}/Bin/ScreenConnect.Client.exe?c={sessionId}",
                $"{baseUrl}/Bin/ScreenConnect.Client.exe?s={sessionId}",
                
                // Original ZIP patterns for backward compatibility
                $"{baseUrl}/Bin/ScreenConnect.WindowsClient.zip",
                $"{baseUrl}/Bin/ScreenConnect.WindowsClient.zip?c={sessionId}",
                $"{baseUrl}/Bin/ScreenConnect.WindowsClient.zip?s={sessionId}",
                
                // Legacy patterns for older configurations
                $"{baseUrl}/Bin/ScreenConnect.ClientService.exe?e=Access&y=Guest&c={sessionId}&h=&k=",
                $"{baseUrl}/Services/ScreenConnectClient.aspx?e=Access&y=Guest&c={sessionId}"
            };
        }

        var defaultPath = Path.Combine(tempDir, $"screenconnect_{sessionId}.zip");

        var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(60); // Increase timeout for large downloads

        foreach (var url in urlPatterns)
        {
            try
            {
                _logger.LogInformation($"🔍 Attempting {operatingSystem} download from: {url}");

                using var response = await httpClient.GetAsync(url);
                
                _logger.LogInformation($"Response status: {response.StatusCode}");
                _logger.LogInformation($"Content-Type: {response.Content.Headers.ContentType}");
                _logger.LogInformation($"Content-Length: {response.Content.Headers.ContentLength}");

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsByteArrayAsync();
                    
                    _logger.LogInformation($"Downloaded {content.Length} bytes");
                    
                    // For macOS, capture client launch parameters from the successful URL
                    if (operatingSystem?.ToLower() == "macos")
                    {
                        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? url;
                        _logger.LogInformation($"macOS download successful from URL: {finalUrl}");
                        _logger.LogInformation($"Original URL: {url}");
                        _logger.LogInformation($"Final URL after redirects: {finalUrl}");
                        
                        _lastDownloadParameters = ExtractClientLaunchParametersFromUrl(finalUrl);
                        if (_lastDownloadParameters != null)
                        {
                            _lastDownloadParameters.s = sessionId; // Ensure session ID is set
                            _logger.LogInformation($"✅ Successfully captured client launch parameters:");
                            _logger.LogInformation($"  Host (h): {_lastDownloadParameters.h}");
                            _logger.LogInformation($"  Port (p): {_lastDownloadParameters.p}");
                            _logger.LogInformation($"  Session (s): {_lastDownloadParameters.s}");
                            _logger.LogInformation($"  Key (k): {(_lastDownloadParameters.k?.Length > 10 ? _lastDownloadParameters.k.Substring(0, 10) + "..." : _lastDownloadParameters.k)}");
                        }
                        else
                        {
                            _logger.LogWarning($"❌ Failed to extract client launch parameters from URL: {finalUrl}");
                        }
                    }
                    
                    // Check if it's a ZIP file (PK header)
                    if (content.Length > 0 && content[0] == 0x50 && content[1] == 0x4B)
                    {
                        await File.WriteAllBytesAsync(defaultPath, content);
                        _logger.LogInformation($"Successfully downloaded ZIP from: {url}");
                        return defaultPath;
                    }
                    // Check if it's an EXE file (MZ header) - sometimes ScreenConnect returns exe directly
                    else if (content.Length > 0 && content[0] == 0x4D && content[1] == 0x5A)
                    {
                        // Save as exe and return it directly - no need to extract
                        var exePath = Path.Combine(tempDir, $"ScreenConnect_{sessionId}.exe");
                        await File.WriteAllBytesAsync(exePath, content);
                        _logger.LogInformation($"Downloaded EXE directly from: {url}");
                        return exePath; // Return exe path instead of zip path
                    }
                    else
                    {
                        // Log the beginning of the content to see what we got
                        var preview = content.Length > 100 ? 
                            System.Text.Encoding.UTF8.GetString(content, 0, 100) : 
                            System.Text.Encoding.UTF8.GetString(content);
                        _logger.LogWarning($"Response from {url} is not a ZIP or EXE file. Content preview: {preview}");
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning($"Failed to download from {url}: HTTP {response.StatusCode} - {errorContent}");
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, $"Request failed for {url}: {ex.Message}");
                continue;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, $"Request timeout for {url}: {ex.Message}");
                continue;
            }
        }

        throw new Exception("Failed to download ScreenConnect client from any known URL pattern. Check if the ScreenConnect instance requires authentication or if the download URLs have changed.");
    }

    public Task<ClientLaunchParameters?> ExtractClientLaunchParametersAsync(string zipPath, string tempDir)
    {
        try
        {
            _logger.LogInformation("Getting client launch parameters from ScreenConnect context API");
            
            // First check if we captured them during download
            if (_lastDownloadParameters != null)
            {
                _logger.LogInformation($"Found client launch parameters from download: h={_lastDownloadParameters.h}, p={_lastDownloadParameters.p}, s={_lastDownloadParameters.s}");
                return Task.FromResult<ClientLaunchParameters?>(_lastDownloadParameters);
            }
            
            // If not available from download, we need to make an API call to get the page context
            // For now, return a constructed example based on HAR file patterns for testing
            _logger.LogWarning("Client launch parameters not available from download - constructing test parameters");
            
            // This is a temporary implementation to test the relay protocol flow
            // In production, this should come from a proper ScreenConnect API call
            var testParameters = new ClientLaunchParameters
            {
                h = "instance-test-relay.screenconnect.com", // This would come from ScreenConnect API
                p = 443,
                k = "TestEncryptionKey", // This would be the real encryption key from ScreenConnect
                s = "", // Will be set by caller
                e = "",
                i = "Test Session",
                n = "",
                r = "",
                a = "",
                l = ""
            };
            
                         _logger.LogInformation($"Using test client launch parameters: h={testParameters.h}");
            return Task.FromResult<ClientLaunchParameters?>(testParameters);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve client launch parameters");
            return Task.FromResult<ClientLaunchParameters?>(null);
        }
    }

    private ClientLaunchParameters? ExtractClientLaunchParametersFromUrl(string url)
    {
        try
        {
            _logger.LogInformation($"🔍 Extracting client launch parameters from URL: {url}");
            
            var uri = new Uri(url);
            _logger.LogInformation($"  URI Path: {uri.AbsolutePath}");
            _logger.LogInformation($"  URI Query: {uri.Query}");
            
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            
            _logger.LogInformation($"  Found {query.AllKeys.Length} query parameters:");
            foreach (var key in query.AllKeys)
            {
                if (key != null)
                {
                    var value = query[key];
                    var displayValue = key == "k" && value?.Length > 20 ? value.Substring(0, 20) + "..." : value;
                    _logger.LogInformation($"    {key}={displayValue}");
                }
            }
            
            // Extract parameters from query string as seen in HAR file
            var parameters = new ClientLaunchParameters
            {
                h = query["h"] ?? "", // relay hostname
                p = int.TryParse(query["p"], out var port) ? port : 443, // port
                k = query["k"] ?? "", // encryption key
                n = query["n"] ?? "", // parameter n
                r = query["r"] ?? "", // parameter r  
                e = query["e"] ?? "", // parameter e
                i = query["i"] ?? "", // session name
                a = query["a"] ?? "", // parameter a
                l = query["l"] ?? ""  // parameter l
            };
            
            // Only return if we have the essential parameters
            if (!string.IsNullOrEmpty(parameters.h) && !string.IsNullOrEmpty(parameters.k))
            {
                _logger.LogInformation($"✅ Successfully extracted required parameters: h={parameters.h}, k=***");
                return parameters;
            }
            
            _logger.LogWarning($"❌ URL missing required parameters (h={parameters.h}, k={!string.IsNullOrEmpty(parameters.k)}): {url}");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"❌ Failed to parse client launch parameters from URL: {url}");
            return null;
        }
    }

    // Store the last successful download URL parameters for relay protocol
    private ClientLaunchParameters? _lastDownloadParameters = null;

    private string ExtractSessionName(string scriptContent)
    {
        try
        {
            // Look for specific session name variable patterns and extract their values
            var sessionNamePatterns = new[]
            {
                // Look for SessionPanel.NewSupportSessionName and similar variables
                @"SessionPanel\.NewSupportSessionName['""\s]*[=:]['""\s]*['""]([^'""]+)['""]",
                @"NewSupportSessionName['""\s]*[=:]['""\s]*['""]([^'""]+)['""]", 
                @"SupportSessionName['""\s]*[=:]['""\s]*['""]([^'""]+)['""]",
                @"sessionName['""\s]*[=:]['""\s]*['""]([^'""]+)['""]",
                
                // Look for direct assignments like: "sessionName": "value"
                @"""sessionName""\s*:\s*""([^""]+)""",
                @"'sessionName'\s*:\s*'([^']+)'",
                
                // Look for session title patterns  
                @"""title""\s*:\s*""([^""]+)""",
                @"'title'\s*:\s*'([^']+)'",
                
                // Look for SC.sessionName assignments
                @"SC\.sessionName\s*=\s*['""]([^'""]+)['""]",
                @"SC\.session\.name\s*=\s*['""]([^'""]+)['""]",
            };

            foreach (var pattern in sessionNamePatterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(scriptContent, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var sessionName = match.Groups[1].Value;
                    
                    if (!string.IsNullOrEmpty(sessionName) && sessionName.Length > 1)
                    {
                        _logger.LogInformation($"🔍 Found session name variable using pattern '{pattern}': {sessionName}");
                        
                        // Skip obviously wrong values (technical terms, not session names)
                        if (sessionName.Contains("SessionGroups") || sessionName.Contains("javascript") ||
                            sessionName.Contains("function") || sessionName.Contains("var ") ||
                            sessionName.Contains("sessionStorage") || sessionName.Contains("sessionId") ||
                            sessionName.Contains(".js") || sessionName.Contains("http") ||
                            sessionName.Length < 3 || sessionName.Length > 100)
                        {
                            _logger.LogInformation($"⏭️ Skipping invalid session name: {sessionName}");
                            continue;
                        }
                        
                        // Construct the full session name as Support/{value}
                        var fullSessionName = $"Support/{sessionName}";
                        _logger.LogInformation($"✅ Constructed session name: {fullSessionName}");
                        return fullSessionName;
                    }
                }
            }

            // Fallback: Look for any "Support/..." patterns directly in the script
            var directPatterns = new[]
            {
                @"""(Support/[^""]+)""|'(Support/[^']+)'",
                @"Support/([A-Za-z0-9\s\-_]+)",
            };

            foreach (var pattern in directPatterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(scriptContent, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var sessionPath = match.Groups[1].Value;
                    if (string.IsNullOrEmpty(sessionPath))
                        sessionPath = match.Groups[2].Value;
                        
                    if (!string.IsNullOrEmpty(sessionPath) && sessionPath.Length > 3 && sessionPath.Length < 100)
                    {
                        // If it's already a full path, use it directly
                        if (sessionPath.StartsWith("Support/"))
                        {
                            _logger.LogInformation($"📝 Found direct Support path: {sessionPath}");
                            return sessionPath;
                        }
                        else
                        {
                            var fullPath = $"Support/{sessionPath}";
                            _logger.LogInformation($"📝 Constructed Support path: {fullPath}");
                            return fullPath;
                        }
                    }
                }
            }

            _logger.LogWarning("❌ Could not extract session name from script content");
            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error extracting session name from script");
            return string.Empty;
        }
    }

    private string ExtractJsonObjectFromPosition(string content, int startIndex)
    {
        try
        {
            var braceCount = 0;
            var inString = false;
            var escaped = false;
            var startPos = startIndex;
            
            for (int i = startIndex; i < content.Length; i++)
            {
                var c = content[i];
                
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                
                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }
                
                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }
                
                if (!inString)
                {
                    if (c == '{')
                    {
                        braceCount++;
                    }
                    else if (c == '}')
                    {
                        braceCount--;
                        if (braceCount == 0)
                        {
                            // Found the end of the JSON object
                            return content.Substring(startPos, i - startPos + 1);
                        }
                    }
                }
            }
            
            _logger.LogWarning("❌ Could not find end of JSON object");
            return "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error extracting JSON object");
            return "";
        }
    }

    public async Task<ClientLaunchParameters?> GetClientLaunchParametersAsync(string sessionId, string baseUrl)
    {
        try
        {
            _logger.LogInformation($"🔍 Getting client launch parameters for session {sessionId} from {baseUrl}");
            
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");
            
            // Step 1: Load the main ScreenConnect page with Session parameter
            var pageUrl = $"{baseUrl}/?Session={sessionId}";
            _logger.LogInformation($"📄 Loading ScreenConnect page: {pageUrl}");
            
            var response = await httpClient.GetAsync(pageUrl);
            
            if (response.IsSuccessStatusCode)
            {
                var pageContent = await response.Content.ReadAsStringAsync();
                
                // Step 2: Extract the Script.ashx URL from the script tag
                var scriptPattern = @"<script src=""(Script\.ashx\?[^""]+)"" id=""defaultScript""></script>";
                var scriptMatch = System.Text.RegularExpressions.Regex.Match(pageContent, scriptPattern);
                
                if (scriptMatch.Success)
                {
                    var scriptUrl = scriptMatch.Groups[1].Value;
                    var fullScriptUrl = $"{baseUrl}/{scriptUrl}";
                    _logger.LogInformation($"📜 Found Script.ashx URL: {fullScriptUrl}");
                    
                    // Step 3: Load the Script.ashx file
                    var scriptResponse = await httpClient.GetAsync(fullScriptUrl);
                    
                    if (scriptResponse.IsSuccessStatusCode)
                    {
                        var scriptContent = await scriptResponse.Content.ReadAsStringAsync();
                        
                        // Step 4: Extract SC.context.clp from the script content
                        // Use a more robust approach to extract the complete clp object
                        var clpStartPattern = @"""clp"":\s*{";
                        var clpStartMatch = System.Text.RegularExpressions.Regex.Match(scriptContent, clpStartPattern);
                        
                        if (clpStartMatch.Success)
                        {
                            var startIndex = clpStartMatch.Index + clpStartMatch.Value.Length - 1; // Position of opening brace
                            var clpJson = ExtractJsonObjectFromPosition(scriptContent, startIndex);
                            
                            if (!string.IsNullOrEmpty(clpJson))
                            {
                                _logger.LogInformation($"✅ Found complete clp object in script");
                                _logger.LogInformation($"🔑 Extracted clp JSON: {clpJson}");
                            
                                var clp = System.Text.Json.JsonSerializer.Deserialize<ClientLaunchParameters>(clpJson);
                                if (clp != null)
                                {
                                    clp.s = sessionId; // Ensure session ID is set
                                    
                                    // If session name (i) is empty, try to extract it from other parts of the script
                                    if (string.IsNullOrEmpty(clp.i))
                                    {
                                        var sessionName = ExtractSessionName(scriptContent);
                                        if (!string.IsNullOrEmpty(sessionName))
                                        {
                                            clp.i = sessionName;
                                            _logger.LogInformation($"📝 Extracted session name from script: {sessionName}");
                                        }
                                    }
                                    
                                    _logger.LogInformation($"✅ Successfully parsed client launch parameters:");
                                    _logger.LogInformation($"  Host (h): {clp.h}");
                                    _logger.LogInformation($"  Port (p): {clp.p}");
                                    _logger.LogInformation($"  Session (s): {clp.s}");
                                    _logger.LogInformation($"  Key (k): {(clp.k?.Length > 20 ? clp.k.Substring(0, 20) + "..." : clp.k)}");
                                    _logger.LogInformation($"  Session Type (e): {clp.e}");
                                    _logger.LogInformation($"  Session Name (i): {clp.i}");
                                    _logger.LogInformation($"  Other params (n,r,a,l): {clp.n},{clp.r},{clp.a},{clp.l}");
                                    return clp;
                                }
                            }
                            
                            _logger.LogWarning("❌ Could not find clp data in Script.ashx content");
                        }
                        else
                        {
                            _logger.LogWarning("❌ Could not find clp pattern in script content");
                        }
                    }
                    else
                    {
                        _logger.LogWarning($"❌ Failed to load Script.ashx: {scriptResponse.StatusCode}");
                    }
                }
                else
                {
                    _logger.LogWarning("❌ Could not find Script.ashx URL in ScreenConnect page");
                }
            }
            else
            {
                _logger.LogWarning($"❌ Failed to load ScreenConnect page: {response.StatusCode}");
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to get client launch parameters from ScreenConnect");
            return null;
        }
    }

    public async Task<bool> TestScreenConnectConnectivity(string baseUrl)
    {
        try
        {
            _logger.LogInformation($"Testing connectivity to ScreenConnect at: {baseUrl}");
            
            var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);
            
            // Test basic connectivity first
            var response = await httpClient.GetAsync(baseUrl);
            _logger.LogInformation($"Base URL test - Status: {response.StatusCode}");
            
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation($"Base URL accessible, content length: {content.Length}");
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to connect to ScreenConnect base URL: {ex.Message}");
            return false;
        }
    }
} 