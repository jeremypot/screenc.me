# Test script for ScreenConnect ZIP Download (Web App)
param(
    [string]$SessionId = "0799b7a1-32a1-4da6-ac06-add294468428",  # From successful session resolution
    [string]$WebAppUrl = "https://pitazappscreenweu01-h2gtamd9hkahg6fn.westeurope-01.azurewebsites.net/api/session/process",
    [string]$ScreenConnectBaseUrl = "https://your-instance.screenconnect.com",
    [string]$OutputDirectory = ".\downloads"
)

Write-Host "Testing ScreenConnect Self-Extracting Package Download (Web App)..." -ForegroundColor Green
Write-Host "Web App URL: $WebAppUrl" -ForegroundColor Yellow
Write-Host "Session ID: $SessionId" -ForegroundColor Yellow
Write-Host "ScreenConnect Base URL: $ScreenConnectBaseUrl" -ForegroundColor Yellow
Write-Host "Output Directory: $OutputDirectory" -ForegroundColor Yellow
Write-Host ""

# Test health endpoint first
$healthUrl = "https://pitazappscreenweu01-h2gtamd9hkahg6fn.westeurope-01.azurewebsites.net/api/health"
try {
    Write-Host "Testing health endpoint..." -ForegroundColor Yellow
    $healthResponse = Invoke-RestMethod -Uri $healthUrl -Method Get -TimeoutSec 10
    Write-Host "✓ Health check: $healthResponse" -ForegroundColor Green
    Write-Host ""
} catch {
    Write-Host "⚠ Health check failed: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host ""
}

# Create output directory if it doesn't exist
if (!(Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    Write-Host "Created output directory: $OutputDirectory" -ForegroundColor Green
}

# Prepare the request body
$requestBody = @{
    sessionId = $SessionId
    screenConnectBaseUrl = $ScreenConnectBaseUrl
} | ConvertTo-Json

Write-Host "Request Body:" -ForegroundColor Cyan
Write-Host $requestBody -ForegroundColor White
Write-Host ""

try {
    Write-Host "Sending request for self-extracting package generation..." -ForegroundColor Yellow
    Write-Host "This may take some time as it needs to download and process files..." -ForegroundColor Yellow
    Write-Host ""
    
    # Set output file path as EXE (self-extracting executable)
    $outputFile = Join-Path $OutputDirectory "ScreenConnect_$SessionId.exe"
    
    # Make the request with file download
    Invoke-WebRequest -Uri $WebAppUrl -Method Post -Body $requestBody -ContentType "application/json" -OutFile $outputFile -TimeoutSec 120
    
    Write-Host "SUCCESS!" -ForegroundColor Green
    Write-Host ""
    
    # Check if file was created and get info
    if (Test-Path $outputFile) {
        $fileInfo = Get-Item $outputFile
        
        Write-Host "File Details:" -ForegroundColor Cyan
        Write-Host "  File Path: $($fileInfo.FullName)" -ForegroundColor White
        Write-Host "  File Size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor White
        Write-Host "  Created: $($fileInfo.CreationTime)" -ForegroundColor White
        Write-Host ""
        
        Write-Host "SUCCESS! Self-extracting EXE downloaded." -ForegroundColor Green
        
    } else {
        Write-Host "ERROR: Output file was not created!" -ForegroundColor Red
    }
    
} catch {
    Write-Host "ERROR!" -ForegroundColor Red
    Write-Host "Error Type: $($_.Exception.GetType().Name)" -ForegroundColor Red
    
    if ($_.Exception -is [System.Net.WebException]) {
        $response = $_.Exception.Response
        if ($response) {
            Write-Host "Status Code: $([int]$response.StatusCode)" -ForegroundColor Red
            Write-Host "Status Description: $($response.StatusDescription)" -ForegroundColor Red
            
            try {
                $streamReader = [System.IO.StreamReader]::new($response.GetResponseStream())
                $errorBody = $streamReader.ReadToEnd()
                $streamReader.Close()
                
                Write-Host "Error Response Body:" -ForegroundColor Red
                Write-Host $errorBody -ForegroundColor White
                
                try {
                    $errorJson = $errorBody | ConvertFrom-Json
                    Write-Host "Formatted Error:" -ForegroundColor Red
                    $errorJson | ConvertTo-Json -Depth 10 | Write-Host -ForegroundColor White
                } catch {
                    # Not JSON, already displayed above
                }
            } catch {
                Write-Host "Could not read error response body" -ForegroundColor Red
            }
        }
    } else {
        Write-Host "Message: $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "Test completed." -ForegroundColor Green 