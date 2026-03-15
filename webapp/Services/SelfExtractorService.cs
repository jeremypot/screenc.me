using System.IO.Compression;

namespace ScreenConnect.WebApp.Services;

public class SelfExtractorService
{
    private readonly ILogger<SelfExtractorService> _logger;
    private static readonly byte[] ZipLocalFileHeader = { 0x50, 0x4B, 0x03, 0x04 };

    public SelfExtractorService(ILogger<SelfExtractorService> logger)
    {
        _logger = logger;
    }

    public async Task<(string filePath, string fileName)> CreateSelfExtractingExecutableAsync(
        string sessionFilesPath, string sessionId, string outputDirectory, string baseUrl, string? sessionCode = null)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            // Get all files from the session directory
            var sessionFiles = Directory.GetFiles(sessionFilesPath, "*", SearchOption.AllDirectories);
            if (!sessionFiles.Any())
            {
                throw new InvalidOperationException("No files found in session directory");
            }

            // CRITICAL FIX: Create proper ApplicationURL.txt with session parameters
            await CreateSessionSpecificApplicationUrl(sessionFilesPath, sessionId, baseUrl, sessionCode);

            // Create output paths
            var outputFileName = $"ScreenConnect_{sessionId}.exe";
            var outputPath = Path.Combine(outputDirectory, outputFileName);

            _logger.LogInformation($"Creating self-extracting EXE for {sessionFiles.Length} files");

            // Get Rust self-extractor template path
            var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "SelfExtractorRust", "screenconnect-extractor.exe");
            if (!File.Exists(templatePath))
            {
                throw new FileNotFoundException($"Rust self-extractor template not found at: {templatePath}");
            }
            
            _logger.LogInformation("Using Rust self-extractor");

            // Create EXE with streaming approach for speed
            await CreateSelfExtractingExecutableStreaming(templatePath, sessionFiles, sessionFilesPath, outputPath);

            stopwatch.Stop();
            _logger.LogInformation($"Self-extracting EXE created in {stopwatch.ElapsedMilliseconds}ms: {outputPath}");
            
            return (outputPath, outputFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to create self-extracting executable after {stopwatch.ElapsedMilliseconds}ms");
            throw;
        }
    }

    private async Task CreateSelfExtractingExecutableStreaming(string templatePath, string[] sessionFiles, string sessionFilesPath, string outputPath)
    {
        // Step 1: Create a proper standalone ZIP in memory first
        byte[] zipData;
        using (var zipStream = new MemoryStream())
        {
            using (var zipArchive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                // Add files sequentially (ZipArchive is not thread-safe for concurrent writes)
                foreach (var file in sessionFiles)
                {
                    var relativePath = Path.GetRelativePath(sessionFilesPath, file);
                    var entry = zipArchive.CreateEntry(relativePath, CompressionLevel.Optimal);
                    
                    using var entryStream = entry.Open();
                    using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 32768);
                    await fileStream.CopyToAsync(entryStream);
                    
                    _logger.LogInformation($"Added to ZIP: {relativePath} ({new FileInfo(file).Length} bytes)");
                }
            }
            
            zipData = zipStream.ToArray();
        }
        
        _logger.LogInformation($"Created standalone ZIP: {zipData.Length} bytes");
        
        // Validate the ZIP before using it
        try
        {
            using var validateStream = new MemoryStream(zipData);
            using var validateArchive = new ZipArchive(validateStream, ZipArchiveMode.Read);
            _logger.LogInformation($"ZIP validation successful: {validateArchive.Entries.Count} entries");
            foreach (var entry in validateArchive.Entries)
            {
                _logger.LogInformation($"ZIP entry: {entry.FullName} ({entry.Length} bytes, compressed: {entry.CompressedLength})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"ZIP validation FAILED: {ex.Message}");
            throw new InvalidOperationException($"Created invalid ZIP file: {ex.Message}");
        }
        
        // Step 2: Combine template + ZIP with proper file streaming
        using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536);
        
        // Copy template EXE directly to output (streaming)
        using (var template = new FileStream(templatePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536))
        {
            await template.CopyToAsync(output);
        }
        
        var zipStartOffset = output.Position;
        _logger.LogInformation($"Template copied, ZIP starts at offset: {zipStartOffset}");

        // Append the standalone ZIP
        await output.WriteAsync(zipData);

        var finalSize = output.Length;
        _logger.LogInformation($"Final EXE size: {finalSize} bytes (Template: {zipStartOffset}, ZIP: {zipData.Length})");
        
        // Verify the math
        if (finalSize != zipStartOffset + zipData.Length)
        {
            _logger.LogError($"Size mismatch! Expected: {zipStartOffset + zipData.Length}, Actual: {finalSize}");
        }
    }

    private async Task CreateSessionSpecificApplicationUrl(string sessionFilesPath, string sessionId, string baseUrl, string? sessionCode)
    {
        var applicationUrlPath = Path.Combine(sessionFilesPath, "ApplicationURL.txt");
        
        _logger.LogInformation($"Creating session-specific ApplicationURL.txt for session: {sessionId}");
        
        // Instead of generating a new URL, copy the working format exactly and just replace the session ID
        // This ensures perfect compatibility with ClickOnce
        var workingUrl = "https://your-instance.screenconnect.com/Bin/ScreenConnect.Client.application?e=Support&y=Guest&h=instance-omt0fe-relay.screenconnect.com&p=443&s=0799b7a1-32a1-4da6-ac06-add294468428&k=BgIAAACkAABSU0ExAAgAAAEAAQA56Mzm9HOtJtkF1fPjqY2NSCJhbPZm7dX9HpYc1Tlpsqe3ZF6pGER%2b3VZpcUgtc2em%2fszPszJn9xmBl9P10HV%2bovxdOZ037cLm7Wm%2f%2frJ4JOE4Aw7ilV0KFtnxy9UKP8P8mMVXpKMSt1W7WdvuR7fKqOjA4wuBw%2fOb4nLOe7oWXqzn5P3rdl%2bPg0CjQvmGp8Wwb8WHb8v7ylvYQJhlTn%2fzHwc66EQQ5Yg1zf%2fYSDGjUoLd6mDdeWIcPtq%2bYv1GOy3lEiz48ErnfPLze1yYvZpuE3lQXySuBGpkGFsSxIh2W2U7pOnSvnVqnsDRGR5iFs39R8EybVHvm6jNiUWRpxPd&r=&i=Untitled%20Session";
        
        // Replace the session ID in the working URL format
        var sessionSpecificUrl = workingUrl.Replace("s=0799b7a1-32a1-4da6-ac06-add294468428", $"s={sessionId}");
        
        // Also update the title if session code is provided
        if (!string.IsNullOrEmpty(sessionCode))
        {
            sessionSpecificUrl = sessionSpecificUrl.Replace("i=Untitled%20Session", $"i=Session%20{Uri.EscapeDataString(sessionCode)}");
        }
        else
        {
            sessionSpecificUrl = sessionSpecificUrl.Replace("i=Untitled%20Session", $"i=Session%20{Uri.EscapeDataString(sessionId)}");
        }
        
        // CRITICAL: Write exactly as UTF-16 LE WITHOUT BOM (like the working version)
        // Use manual byte writing to ensure absolutely no BOM
        var utf16Bytes = System.Text.Encoding.Unicode.GetBytes(sessionSpecificUrl);
        
        // Verify no BOM was added
        if (utf16Bytes.Length >= 2 && utf16Bytes[0] == 0xFF && utf16Bytes[1] == 0xFE)
        {
            _logger.LogError("BOM detected in UTF-16 encoding! This will break ClickOnce.");
            throw new InvalidOperationException("BOM was incorrectly added to ApplicationURL.txt");
        }
        
        await File.WriteAllBytesAsync(applicationUrlPath, utf16Bytes);
        
        // Verify the file was written correctly
        var verification = System.Text.Encoding.Unicode.GetString(await File.ReadAllBytesAsync(applicationUrlPath));
        if (verification != sessionSpecificUrl)
        {
            _logger.LogError($"ApplicationURL.txt verification failed! Expected: {sessionSpecificUrl}, Got: {verification}");
            throw new InvalidOperationException("ApplicationURL.txt was not written correctly");
        }
        
        _logger.LogInformation($"Created ApplicationURL.txt with session ID: {sessionId}");
        _logger.LogInformation($"Full URL: {sessionSpecificUrl}");
        _logger.LogInformation($"File size: {new FileInfo(applicationUrlPath).Length} bytes");
    }
}