# ScreenConnect Frontend

A modern, responsive frontend for the ScreenConnect Session Downloader built with Tailwind CSS.

## 🚀 Quick Start

```bash
# Navigate to frontend directory
cd src

# Install dependencies
npm install

# Start development server
npm run dev
```

## 📁 Project Structure

```
src/
├── index.html              # Main HTML file
├── script.js               # JavaScript functionality
├── style.css               # Legacy CSS (replaced by Tailwind)
├── staticwebapp.config.json # Azure Static Web Apps configuration
├── package.json            # Frontend dependencies and scripts
└── README.md               # This file
```

## 🎨 Features

- **Modern Design**: Tailwind CSS with gradient backgrounds and animations
- **Auto-Download**: Detects URL parameters and starts downloads automatically
- **Session Modes**: Support for both session codes and full session IDs, both requiring instance configuration
- **Progress Indicators**: Real-time download progress with animations
- **Responsive**: Works on all device sizes
- **Accessible**: Keyboard navigation and ARIA support

## 🛠️ Available Scripts

- `npm run dev` - Build CSS and start development server on port 3000 (opens browser)
- `npm run start` - Same as dev
- `npm run serve` - Start server on port 8080 (no auto-open)
- `npm run build` - Build optimized Tailwind CSS for production
- `npm run build:css` - Build Tailwind CSS once
- `npm run build:css:watch` - Build Tailwind CSS and watch for changes
- `npm run dev:watch` - Build CSS with watch mode and start development server

## 🔧 Configuration

### API Endpoints
Update the `CONFIG` object in `script.js`:

```javascript
const CONFIG = {
    API_BASE_URL: 'https://your-webapp.azurewebsites.net/api'
};
```

### URL Parameters
- `?session=<sessionId>&instance=<hostname>` - Auto-download for specific session with instance
- `?instance=<hostname>` - Pre-fill ScreenConnect instance (user enters session code/ID)

## 🌐 Example URLs

```
# Auto-download with instance
https://yoursite.com?session=7758d589-4f39-48d8-ad81-3fbae8e049fd&instance=your-instance.screenconnect.com

# Pre-fill instance (user enters code/session)
https://yoursite.com?instance=your-instance.screenconnect.com

# User enters both instance and code/session
https://yoursite.com
```

## 🚀 Deployment

### Azure Static Web Apps
1. Deploy the `src/` directory
2. Use the included `staticwebapp.config.json`
3. Configure API integration with the webapp backend

### Other Static Hosts
Deploy the contents of the `src/` directory to:
- Netlify
- Vercel
- GitHub Pages
- AWS S3 + CloudFront
- Any static hosting provider

## 🔒 Security

- Content Security Policy configured for Tailwind CDN
- CORS headers for API integration
- No sensitive data stored client-side

## 🎯 API Integration

This frontend expects the following API endpoints:

- `POST /api/session/resolve-code` - Convert session code to session ID
- `POST /api/session/process` - Download ScreenConnect client

See the webapp project for backend implementation.

## 🐛 Troubleshooting

### CORS Issues
Ensure your backend webapp has CORS configured for your frontend domain.

### API Errors
Check the browser console for detailed error messages and verify the API_BASE_URL is correct.

### Styling Issues
This project uses Tailwind CSS from CDN. If styles aren't loading, check your Content Security Policy.

## 📞 Support

For questions or issues, contact: [info@screenc.me](mailto:info@screenc.me) 