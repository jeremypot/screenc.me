# ScreenConnect Linux Web App

ASP.NET Core Web API (Linux) for the ScreenConnect session downloader. It downloads the ScreenConnect client from your instance and returns either the native executable or a self-extracting executable built with the Rust extractor.

## Features

- **Session Code Resolution**: Convert ScreenConnect session codes to session IDs
- **Self-Extracting Executables**: Create Windows self-extracting .exe using the Rust extractor (built in Docker)
- **Native EXE Passthrough**: When the instance serves an EXE directly, returns it with the original filename (e.g. `ScreenConnect.Client.exe`)
- **Docker-based Deployment**: Alpine-based image with .NET 8 and Rust-built extractor
- **Azure Web App**: Deploy as a Linux Web App for Containers

## Architecture

| Aspect | This Web App |
|--------|---------------|
| **Runtime** | ASP.NET Core 8.0 Web API |
| **OS** | Linux (Alpine in Docker) |
| **Self-Extractor** | Rust (SelfExtractorRust), cross-compiled to Windows x64 in build |
| **Deployment** | Docker → Azure Container Registry → Azure Web App for Containers |

## API Endpoints

### Base URL
Set per environment (e.g. `https://<your-webapp-name>.azurewebsites.net`). The frontend is configured with `API_BASE_URL` pointing to this hostname.

### Endpoints

#### POST `/api/session/resolve-code`
Resolve a ScreenConnect session code to get the session ID.

**Request:**
```json
{
  "sessionCode": "99991",
  "screenConnectBaseUrl": "https://your-instance.screenconnect.com"
}
```

**Response:**
```json
{
  "sessionId": "7758d589-4f39-48d8-ad81-3fbae8e049fd",
  "sessionCode": "99991"
}
```

#### POST `/api/session/process`
Download ScreenConnect client and create a self-extracting executable.

**Request:**
```json
{
  "sessionId": "7758d589-4f39-48d8-ad81-3fbae8e049fd",
  "screenConnectBaseUrl": "https://your-instance.screenconnect.com"
}
```

**Response:**
Binary file download: `ScreenConnect_{sessionId}.exe`

#### GET `/api/session/system-info`
Get system information and dependency status.

**Response:**
```json
{
  "platform": "Unix",
  "osVersion": "Linux 5.4.0",
  "sevenZipInstalled": true,
  "runtimeVersion": "8.0.0",
  "timestamp": "2024-01-15T10:30:00Z"
}
```

#### GET `/api/session/test-connectivity?baseUrl={url}`
Test connectivity to a ScreenConnect instance.

## Self-Extracting Executable (Rust)

When the ScreenConnect instance provides a ZIP (legacy), the backend packages it into a Windows self-extracting executable:

- **Rust extractor**: Built in Docker (Alpine + mingw) from `SelfExtractorRust/`. Small, no .NET runtime on client.
- **Flow**: User runs `ScreenConnect_{sessionId}.exe` → extractor unpacks embedded ZIP → runs `ScreenConnect.Client.exe`.
- **When instance returns EXE directly**: Backend returns that EXE unchanged with its original filename (e.g. `ScreenConnect.Client.exe`) so the browser does not rename it.

## Development

### Prerequisites
- .NET 8.0 SDK
- Docker Desktop
- Azure CLI (for deployment)

### Local Development

1. **Clone and navigate:**
   ```bash
   cd webapp
   ```

2. **Restore dependencies:**
   ```bash
   dotnet restore
   ```

3. **Run locally:**
   ```bash
   dotnet run
   ```

4. **Test endpoints:**
   ```bash
   curl -X GET http://localhost:5000/api/session/system-info
   ```

### Docker Development

1. **Build image:**
   ```bash
   docker build -t screenconnect-webapp .
   ```

2. **Run container:**
   ```bash
   docker run -p 8080:8080 screenconnect-webapp
   ```

3. **Test system info:**
   ```bash
   curl -X GET http://localhost:8080/api/session/system-info
   ```

## Deployment

### Azure Container Registry Setup

1. **Create container registry:**
   ```bash
   az acr create --resource-group rg-screenconnect --name yourregistry --sku Basic
   ```

2. **Login to registry:**
   ```bash
   az acr login --name yourregistry
   ```

3. **Build and push:**
   ```bash
   docker build -t yourregistry.azurecr.io/screenconnect-webapp:latest .
   docker push yourregistry.azurecr.io/screenconnect-webapp:latest
   ```

### Azure Web App Deployment

1. Create a Web App for Containers (Linux) and Azure Container Registry.
2. Use the **azure-pipelines-webapp.yml** pipeline to build and push the image, then deploy the container to the Web App.
3. **Verify:** `curl -X GET https://<your-webapp-name>.azurewebsites.net/api/session/system-info`

### Azure DevOps Pipeline

The root `azure-pipelines-webapp.yml`:

1. Builds the Docker image from `webapp/Dockerfile`
2. Runs Trivy vulnerability scan
3. Pushes the image to Azure Container Registry
4. Deploys the container to the Azure Web App (production on `main`)

**Variable group (ScreenConnect-Variables):** `AZURE_SERVICE_CONNECTION`, `WEB_APP_NAME`, `CONTAINER_REGISTRY_CONNECTION`, `CONTAINER_REGISTRY_URL`, `CONTAINER_REGISTRY_USERNAME`, `CONTAINER_REGISTRY_PASSWORD`, `SCREENCONNECT_BASE_URL`.

## Dependencies

### Docker Image (Dockerfile)
- **Base**: `mcr.microsoft.com/dotnet/aspnet:8.0-alpine`
- **Build**: .NET 8 SDK, Rust (rustup), mingw-w64-gcc for cross-compiling the Windows self-extractor

### .NET Dependencies
- `Microsoft.AspNetCore.Cors`: Cross-origin request support
- `Newtonsoft.Json`: JSON serialization
- `System.IO.Compression`: ZIP file handling

## Troubleshooting

### Common Issues

#### Container Permissions
```
Permission denied: /tmp/screenconnect
```
**Solution:** Dockerfile creates `/tmp/screenconnect` and sets ownership for the app user (1654).

#### Rust Build Fails in Docker
**Solution:** Ensure Docker build uses Linux (e.g. `ubuntu-latest` or Alpine with build-base/mingw-w64-gcc). Pipeline uses `ubuntu-latest` for the Docker build.

### Logs and Monitoring

**View Azure Web App logs:** Azure Portal → Web App → Log stream (or Monitoring / Log Analytics).

## Security Considerations

- **HTTPS Only**: All endpoints enforce HTTPS
- **CORS**: Configured for cross-origin requests
- **Container Security**: Runs as non-root user (`www-data`)
- **Resource Limits**: Controlled temporary directory access
- **Input Validation**: Request payload validation

## Future Enhancements

- [ ] Application Insights integration
- [ ] Redis cache for session data
- [ ] Azure Key Vault for secrets
- [ ] SFX template caching