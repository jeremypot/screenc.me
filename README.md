# ScreenConnect Session Downloader

An Azure-based solution for downloading and repackaging ScreenConnect session files as self-extracting executables.

## Architecture

- **Frontend**: Azure Static Web App (HTML/CSS/JavaScript, Tailwind CSS)
- **Backend**: Azure Web App (ASP.NET Core .NET 8.0, Linux) running in Docker
- **CI/CD**: Azure DevOps pipelines for frontend and webapp
- **Infrastructure**: Azure Container Registry, Azure Web App for Containers, Static Web App

## Features

- **Session Code Input**: Default interface for entering session codes (e.g. 99991)
- **Session ID Detection**: Detects session ID from URL query parameter
- **Code Resolution**: Resolves session codes to full session IDs via ScreenConnect API
- **Instance Parameter**: Optional instance URL for multi-tenant usage
- **Dual Input Modes**: Session code (default) or full session ID
- **Auto Download**: Downloads client from ScreenConnect instance; returns native EXE or self-extracting executable
- **Filename Preservation**: Downloaded files keep the name from the API (e.g. `ScreenConnect.Client.exe`) to avoid Windows "renamed file" warnings
- **CORS Support**: Configured for cross-origin requests from the frontend

## Usage

### Session Code Entry (Default Mode)

**Default instance:**  
`https://your-static-web-app.azurestaticapps.net` → Enter session code: `99991`

**Custom instance:**  
`https://your-static-web-app.azurestaticapps.net?instance=your-instance.screenconnect.com` → Enter session code: `99991`

### Direct Session ID Access

**With custom instance:**  
`https://your-static-web-app.azurestaticapps.net?instance=your-instance.screenconnect.com&session=7758d589-4f39-48d8-ad81-3fbae8e049fd`

**With default instance:**  
`https://your-static-web-app.azurestaticapps.net?session=7758d589-4f39-48d8-ad81-3fbae8e049fd`

### Manual Entry

1. **Session code**: Enter a 4–6 digit code (e.g. 99991)
2. **Full session ID**: Use "Have a full session ID instead?" to enter the full session details
3. **Instance**: Set via URL parameter `?instance=hostname` or in the form

## ScreenConnect URL Format

- **Instance**: `your-instance.screenconnect.com` (domain/subdomain)
- **Session ID**: GUID after `Session=` in the URL

Example: `https://your-instance.screenconnect.com/?Session=7758d589-4f39-48d8-ad81-3fbae8e049fd`

## Project Structure

```
├── frontend/                 # Static Web App
│   ├── src/
│   │   ├── index.html
│   │   ├── script.js
│   │   ├── style.css
│   │   └── staticwebapp.config.json
│   ├── package.json
│   └── README.md
├── webapp/                   # ASP.NET Core Web API (Linux)
│   ├── Controllers/          # SessionController, HealthController
│   ├── Models/
│   ├── Services/             # ScreenConnectService, SelfExtractorService
│   ├── SelfExtractorRust/     # Rust-built Windows self-extractor
│   ├── Dockerfile
│   ├── Program.cs
│   └── README.md
├── azure-pipelines-frontend.yml
├── azure-pipelines-webapp.yml
├── SCREENCONNECT.sln
└── README.md
```

## Deployment

### Prerequisites

- Azure subscription
- Azure DevOps project and service connection to Azure
- Variable group `ScreenConnect-Variables` (see below)

### Variable Group: ScreenConnect-Variables

- `AZURE_SERVICE_CONNECTION`: Azure Resource Manager service connection name
- `API_BASE_URL`: Web App hostname (e.g. `screenconnect-webapp-prod.azurewebsites.net`) for frontend API URL
- `SCREENCONNECT_BASE_URL`: Default ScreenConnect instance URL (optional)
- `AZURE_STATIC_WEB_APPS_API_TOKEN`: Static Web App deployment token (secret)
- **Web App pipeline only:** `WEB_APP_NAME`, `CONTAINER_REGISTRY_CONNECTION`, `CONTAINER_REGISTRY_URL`, `CONTAINER_REGISTRY_USERNAME`, `CONTAINER_REGISTRY_PASSWORD`

### Azure Resources

1. **Web App**: Create Azure Web App for Containers (Linux, .NET 8 / custom container). Pipeline builds the image from `webapp/Dockerfile` and deploys it.
2. **Static Web App**: Create Azure Static Web App. Frontend pipeline deploys `frontend/src`.
3. **Container Registry**: For the webapp pipeline to push the Docker image.

### Pipelines

- **azure-pipelines-frontend.yml**: On changes under `frontend/src/`, replaces API placeholder in `script.js` with `API_BASE_URL` and deploys to Static Web App.
- **azure-pipelines-webapp.yml**: On changes under `webapp/`, builds Docker image, runs Trivy scan, pushes to registry, deploys container to Web App.

## Configuration

### Frontend

`frontend/src/script.js`:

```javascript
const CONFIG = {
    API_BASE_URL: 'https://your-webapp.azurewebsites.net/api'
};
```

The pipeline overwrites this at deploy time using the `API_BASE_URL` variable.

### Backend (Web App)

- `SCREENCONNECT_BASE_URL`: Default ScreenConnect instance (optional)
- Container runs ASP.NET Core on port 8080 (see Dockerfile).

## API Endpoints (Web App)

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/session/resolve-code` | Resolve session code to session ID |
| POST | `/api/session/process` | Download client and return EXE or self-extracting executable |
| GET | `/api/session/system-info` | System and dependency info |
| GET | `/api/session/test-connectivity?baseUrl={url}` | Test connectivity to a ScreenConnect instance |
| GET | `/api/health` | Health check |

## Security

- **CORS**: Web App allows configured origins; restrict as needed for production.
- **HTTPS**: Use HTTPS for frontend and backend.
- **Input validation**: Session ID format and request payloads are validated.
- **Container**: Runs as non-root user in Docker.

## Troubleshooting

1. **Download or processing fails**  
   Check Web App logs (Azure Portal → Web App → Log stream / Log Analytics). Verify session ID format and that the ScreenConnect instance is reachable.

2. **Frontend cannot reach API**  
   Confirm `API_BASE_URL` in the variable group and that CORS on the Web App includes your Static Web App origin.

3. **Self-extracting EXE**  
   Backend uses the Rust self-extractor in `webapp/SelfExtractorRust`. See `webapp/README.md` and `webapp/SelfExtractorRust/README.md`.

## Development

### Web App (local)

```bash
cd webapp
dotnet restore
dotnet run
# API: http://localhost:5000 (or port shown in console)
```

### Frontend (local)

```bash
cd frontend
npm install
npm run dev
```

Set `CONFIG.API_BASE_URL` in `frontend/src/script.js` to your local or deployed Web App URL (e.g. `http://localhost:5000/api`).

### Docker (webapp)

```bash
cd webapp
docker build -t screenconnect-webapp .
docker run -p 8080:8080 screenconnect-webapp
```

## License

MIT. This project is not affiliated with ConnectWise or ScreenConnect.
