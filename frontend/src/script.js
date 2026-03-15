// Configuration
const CONFIG = {
    API_BASE_URL: 'https://your-webapp.azurewebsites.net/api'
};

// Current state management
let currentProgress = 0;
let progressInterval = null;

// OS Detection (simplified)
function detectOperatingSystem() {
    const platform = navigator.platform.toLowerCase();
    const userAgent = navigator.userAgent.toLowerCase();
    
    if (/android/.test(userAgent)) return 'android';
    if (/iphone|ipad|ipod/.test(userAgent) || (platform === 'macintel' && navigator.maxTouchPoints > 1)) return 'ios';
    if (platform.includes('mac') || userAgent.includes('mac')) return 'macos';
    if (platform.includes('win') || userAgent.includes('windows')) return 'windows';
    if (platform.includes('linux') || userAgent.includes('linux')) return 'linux';
    
    return 'windows'; // Default
}

// Session storage helpers
function saveInstanceToStorage(instance) {
    if (instance && instance.trim()) {
        localStorage.setItem('screenconnect_instance', instance.trim());
    }
}

function loadInstanceFromStorage() {
    return localStorage.getItem('screenconnect_instance') || '';
}

// Initialize the application
document.addEventListener('DOMContentLoaded', function() {
    // Load saved instance
    const savedInstance = loadInstanceFromStorage();
    if (savedInstance) {
        document.getElementById('instanceInput').value = savedInstance;
    }
    
    // Check for URL parameters
    const urlParams = new URLSearchParams(window.location.search);
    const sessionId = urlParams.get('session');
    const sessionCode = urlParams.get('code');
    const instanceUrl = urlParams.get('instance');
    
    // Pre-fill from URL parameters
    if (instanceUrl) {
        document.getElementById('instanceInput').value = instanceUrl;
        saveInstanceToStorage(instanceUrl);
    }
    
    if (sessionCode) {
        document.getElementById('sessionCodeInput').value = sessionCode;
    }
    
    // Auto-process if both parameters are provided
    if (instanceUrl && (sessionCode || sessionId)) {
        setTimeout(() => {
            if (sessionCode) {
                initiateConnection();
            } else if (sessionId) {
                // Handle direct session ID (convert to connection flow)
                processDirectSession(sessionId, instanceUrl);
            }
        }, 500);
    }
    
    // Add event listeners
    setupEventListeners();
    
    // Track page view
    if (typeof posthog !== 'undefined') {
        posthog.capture('page_viewed', {
            page: 'connection_portal'
        });
    }
});

// Setup event listeners
function setupEventListeners() {
    // Enter key handling
    document.getElementById('instanceInput').addEventListener('keypress', function(e) {
        if (e.key === 'Enter') {
            document.getElementById('sessionCodeInput').focus();
        }
    });
    
    document.getElementById('sessionCodeInput').addEventListener('keypress', function(e) {
        if (e.key === 'Enter') {
            initiateConnection();
        }
    });
    
    // Save instance to storage when it changes
    document.getElementById('instanceInput').addEventListener('input', function(e) {
        saveInstanceToStorage(e.target.value);
    });
    
    // Close overlay when clicking outside
    document.getElementById('successOverlay').addEventListener('click', function(e) {
        if (e.target === this) {
            hideSuccessOverlay();
        }
    });
    
    // Share button event listener
    const shareBtn = document.getElementById('shareBtn');
    if (shareBtn) {
        console.log('Share button found, attaching event listener'); // Debug
        shareBtn.addEventListener('click', function(e) {
            console.log('Share button click event fired'); // Debug
            e.preventDefault();
            e.stopPropagation();
            
            // Visual feedback that button was clicked
            const originalText = shareBtn.querySelector('span').textContent;
            shareBtn.querySelector('span').textContent = 'Creating link...';
            shareBtn.style.opacity = '0.5';
            
            // Call share function
            shareConnection();
            
            // Reset button after a short delay
            setTimeout(() => {
                shareBtn.querySelector('span').textContent = originalText;
                shareBtn.style.opacity = '';
            }, 1000);
        });
    } else {
        console.error('Share button not found!'); // Debug
    }
}

// Main connection initiation function
async function initiateConnection() {
    const instanceInput = document.getElementById('instanceInput');
    const sessionCodeInput = document.getElementById('sessionCodeInput');
    const instanceUrl = instanceInput.value.trim();
    const sessionCode = sessionCodeInput.value.trim();
    
    // Validation
    if (!instanceUrl) {
        showError('Please enter your server address');
        instanceInput.focus();
        return;
    }
    
    if (!sessionCode) {
        showError('Please enter your access code');
        sessionCodeInput.focus();
        return;
    }
    
    // Validate session code format
    if (!/^\d{4,6}$/.test(sessionCode)) {
        showError('Access code should be 4-6 digits');
        sessionCodeInput.focus();
        return;
    }
    
    // Save instance for next time
    saveInstanceToStorage(instanceUrl);
    
    // Track connection attempt
    if (typeof posthog !== 'undefined') {
        posthog.capture('connection_initiated', {
            instance_domain: instanceUrl.split('.')[0]
        });
    }
    
    // Start connection process
    await processConnection(instanceUrl, sessionCode);
}

// Process the connection
async function processConnection(instanceUrl, sessionCode) {
    try {
        showLoading();
        
        // Step 1: Resolve session code
        updateProgress(20, 'Validating access code...');
        
        const sessionId = await resolveSessionCode(sessionCode, instanceUrl);
        
        // Step 2: Detect OS and process session
        updateProgress(50, 'Preparing connection...');
        
        const currentOS = detectOperatingSystem();
        const sessionData = {
            sessionId: sessionId,
            screenConnectBaseUrl: `https://${instanceUrl}`,
            operatingSystem: currentOS,
            sessionCode: sessionCode
        };
        
        // Step 3: Handle based on OS
        if (currentOS === 'ios' || currentOS === 'android' || currentOS === 'macos') {
            await handleMobileDesktopConnection(sessionData);
        } else {
            await handleWindowsConnection(sessionData);
        }
        
    } catch (error) {
        console.error('Connection failed:', error);
        showError(error.message || 'Connection failed. Please check your details and try again.');
        
        // Track error
        if (typeof posthog !== 'undefined') {
            posthog.capture('connection_failed', {
                error_message: error.message,
                instance_domain: instanceUrl.split('.')[0]
            });
        }
    }
}

// Process direct session ID (from URL)
async function processDirectSession(sessionId, instanceUrl) {
    try {
        showLoading();
        updateProgress(30, 'Connecting to session...');
        
        const currentOS = detectOperatingSystem();
        const sessionData = {
            sessionId: sessionId,
            screenConnectBaseUrl: `https://${instanceUrl}`,
            operatingSystem: currentOS
        };
        
        if (currentOS === 'ios' || currentOS === 'android' || currentOS === 'macos') {
            await handleMobileDesktopConnection(sessionData);
        } else {
            await handleWindowsConnection(sessionData);
        }
        
    } catch (error) {
        console.error('Direct session failed:', error);
        showError('Session connection failed. Please try again.');
    }
}

// Resolve session code to session ID
async function resolveSessionCode(sessionCode, instanceUrl) {
    const response = await fetch(`${CONFIG.API_BASE_URL}/session/resolve-code`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
        },
        body: JSON.stringify({
            sessionCode: sessionCode,
            screenConnectBaseUrl: `https://${instanceUrl}`
        })
    });
    
    if (!response.ok) {
        const errorData = await response.json().catch(() => ({}));
        throw new Error(errorData.error || 'Invalid access code or server address');
    }
    
    const data = await response.json();
    if (!data.sessionId) {
        throw new Error('Access code not found. Please check your code and try again.');
    }
    
    return data.sessionId;
}

// Handle mobile/desktop connection (iOS, Android, macOS)
async function handleMobileDesktopConnection(sessionData) {
    updateProgress(70, 'Testing app connection...');
    
    try {
        // Try to get client launch parameters for app protocol
        const response = await fetch(`${CONFIG.API_BASE_URL}/session/process`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(sessionData)
        });

        if (!response.ok) {
            throw new Error('Failed to get session information');
        }

        const clientLaunchParamsHeader = response.headers.get('X-Client-Launch-Parameters');
        
        if (clientLaunchParamsHeader) {
            updateProgress(85, 'Attempting instant connection...');
            
            const clientLaunchParameters = JSON.parse(clientLaunchParamsHeader);
            const appSuccess = await testAppProtocol(clientLaunchParameters, sessionData.operatingSystem);
            
            if (appSuccess) {
                updateProgress(100, 'Connected successfully!');
                setTimeout(() => {
                    showSuccess('Connection established! The app should have opened automatically.');
                }, 500);
                return;
            }
        }
        
        // App protocol failed, download file
        updateProgress(90, 'Preparing download...');
        await downloadSessionFile(response);
        
    } catch (error) {
        console.error('Mobile/desktop connection failed:', error);
        throw error;
    }
}

// Handle Windows connection
async function handleWindowsConnection(sessionData) {
    updateProgress(70, 'Preparing download...');
    
    const response = await fetch(`${CONFIG.API_BASE_URL}/session/process`, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
        },
        body: JSON.stringify(sessionData)
    });
    
    if (!response.ok) {
        throw new Error('Failed to prepare connection file');
    }
    
    updateProgress(90, 'Downloading connection file...');
    await downloadSessionFile(response);
}

// Parse filename from Content-Disposition header (filename="..." or filename*=UTF-8''...)
function getFilenameFromContentDisposition(headerValue) {
    if (!headerValue || typeof headerValue !== 'string') return null;
    const filenameStar = headerValue.match(/filename\*=(?:UTF-8'')?([^;\s]+)/i);
    if (filenameStar && filenameStar[1]) {
        try {
            return decodeURIComponent(filenameStar[1].replace(/^["']|["']$/g, ''));
        } catch (_) {
            // ignore decode errors
        }
    }
    const filename = headerValue.match(/filename=(["']?)([^;"'\n]+)\1/);
    if (filename && filename[2]) {
        return filename[2].trim().replace(/^["']|["']$/g, '');
    }
    return null;
}

// Download session file
async function downloadSessionFile(response) {
    const blob = await response.blob();
    const currentOS = detectOperatingSystem();
    
    // Use filename from API when present to avoid "renamed file" security warning
    const contentDisposition = response.headers.get('Content-Disposition');
    const apiFilename = getFilenameFromContentDisposition(contentDisposition);
    const fallbackFilename = currentOS === 'macos' ? 'ScreenConnect.zip' : 'ScreenConnect.exe';
    const filename = apiFilename && apiFilename.length > 0 ? apiFilename : fallbackFilename;
    
    // Create download link
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.style.display = 'none';
    a.href = url;
    a.download = filename;
    
    document.body.appendChild(a);
    a.click();
    
    // Clean up
    window.URL.revokeObjectURL(url);
    document.body.removeChild(a);
    
    updateProgress(100, 'Download complete!');
    
    setTimeout(() => {
        const message = currentOS === 'macos' 
            ? 'Extract the ZIP file and run the app to connect'
            : 'Run the downloaded file to start your remote session';
            
        showSuccess(message);
        showSuccessOverlay();
    }, 500);
    
    // Track successful download
    if (typeof posthog !== 'undefined') {
        posthog.capture('file_downloaded', {
            os: currentOS,
            file_type: currentOS === 'macos' ? 'zip' : 'exe'
        });
    }
}

// Test app protocol for mobile/desktop
function testAppProtocol(clientLaunchParameters, osType) {
    return new Promise((resolve) => {
        try {
            const k = encodeURIComponent(clientLaunchParameters.k || '');
            const sessionName = clientLaunchParameters.i || '';
            let protocolUrl = '';
            
            if (osType === 'android') {
                const relayUrl = `relay://${clientLaunchParameters.h}:${clientLaunchParameters.p}/${clientLaunchParameters.s}/${k}///${sessionName}//`;
                protocolUrl = relayUrl.replace('relay:', 'intent:') + 
                             '#Intent;scheme=relay;package=com.screenconnect.androidclient;end';
            } else {
                protocolUrl = `relay://${clientLaunchParameters.h}:${clientLaunchParameters.p}/${clientLaunchParameters.s}/${k}///${sessionName}//`;
            }
            
            // Try to launch the protocol
            const iframe = document.createElement('iframe');
            iframe.style.display = 'none';
            iframe.src = protocolUrl;
            document.body.appendChild(iframe);
            
            // Clean up and assume success for protocol test
            setTimeout(() => {
                if (iframe.parentNode) {
                    document.body.removeChild(iframe);
                }
                resolve(true); // Optimistically assume success
            }, 1000);
            
        } catch (e) {
            console.error('Protocol test failed:', e);
            resolve(false);
        }
    });
}

// UI State Management
function showLoading() {
    hideAllStates();
    document.getElementById('loadingCard').classList.remove('hidden');
    startProgressAnimation();
}

function showSuccess(message) {
    hideAllStates();
    document.getElementById('successMessage').textContent = message;
    document.getElementById('successCard').classList.remove('hidden');
    stopProgressAnimation();
}

function showError(message) {
    hideAllStates();
    document.getElementById('errorMessage').textContent = message;
    document.getElementById('errorCard').classList.remove('hidden');
    stopProgressAnimation();
}

function hideAllStates() {
    document.getElementById('inputCard').classList.add('hidden');
    document.getElementById('loadingCard').classList.add('hidden');
    document.getElementById('successCard').classList.add('hidden');
    document.getElementById('errorCard').classList.add('hidden');
}

function resetToStart() {
    hideAllStates();
    document.getElementById('inputCard').classList.remove('hidden');
    
    // Clear session code but keep instance
    document.getElementById('sessionCodeInput').value = '';
    
    // Clear URL parameters
    if (window.history.replaceState) {
        window.history.replaceState({}, document.title, window.location.pathname);
    }
    
    // Reset progress
    updateProgress(0, 'Ready to connect...');
    stopProgressAnimation();
    
    // Focus on session code input
    setTimeout(() => {
        const instanceInput = document.getElementById('instanceInput');
        if (instanceInput.value.trim()) {
            document.getElementById('sessionCodeInput').focus();
        } else {
            instanceInput.focus();
        }
    }, 100);
}

// Progress Management
function updateProgress(percentage, message) {
    currentProgress = percentage;
    document.getElementById('progressBar').style.width = `${percentage}%`;
    document.getElementById('loadingMessage').textContent = message;
    
    // Update loading title based on progress
    const loadingTitle = document.getElementById('loadingTitle');
    if (percentage < 30) {
        loadingTitle.textContent = 'Connecting...';
    } else if (percentage < 70) {
        loadingTitle.textContent = 'Processing...';
    } else if (percentage < 100) {
        loadingTitle.textContent = 'Almost ready...';
    } else {
        loadingTitle.textContent = 'Complete!';
    }
}

function startProgressAnimation() {
    if (progressInterval) clearInterval(progressInterval);
    
    progressInterval = setInterval(() => {
        // Add some natural variation to progress
        if (currentProgress < 95) {
            const increment = Math.random() * 2;
            currentProgress = Math.min(95, currentProgress + increment);
            document.getElementById('progressBar').style.width = `${currentProgress}%`;
        }
    }, 200);
}

function stopProgressAnimation() {
    if (progressInterval) {
        clearInterval(progressInterval);
        progressInterval = null;
    }
}

// Success Overlay
function showSuccessOverlay() {
    const overlay = document.getElementById('successOverlay');
    overlay.classList.remove('hidden');
    
    // Add animation
    setTimeout(() => {
        overlay.querySelector('div').classList.add('scale-100');
        overlay.querySelector('div').classList.remove('scale-95');
    }, 10);
    
    // Auto-hide after 5 seconds
    setTimeout(() => {
        hideSuccessOverlay();
    }, 5000);
}

function hideSuccessOverlay() {
    const overlay = document.getElementById('successOverlay');
    const content = overlay.querySelector('div');
    
    content.classList.remove('scale-100');
    content.classList.add('scale-95');
    
    setTimeout(() => {
        overlay.classList.add('hidden');
    }, 300);
}

// Share Connection Functionality
function shareConnection() {
    console.log('Share button clicked'); // Debug
    
    try {
        const instanceInput = document.getElementById('instanceInput');
        const sessionCodeInput = document.getElementById('sessionCodeInput');
        
        if (!instanceInput || !sessionCodeInput) {
            console.error('Input elements not found');
            showTemporaryMessage('Error: Form elements not found', 'error');
            return;
        }
        
        const instanceUrl = instanceInput.value.trim();
        const sessionCode = sessionCodeInput.value.trim();
        
        console.log('Instance:', instanceUrl, 'Code:', sessionCode); // Debug
        
        // Check if we have at least the instance
        if (!instanceUrl) {
            showTemporaryMessage('Please enter a server address first', 'warning');
            instanceInput.focus();
            return;
        }
        
        // Build the share URL
        const currentUrl = window.location.origin + window.location.pathname;
        const params = new URLSearchParams();
        
        params.set('instance', instanceUrl);
        if (sessionCode) {
            params.set('code', sessionCode);
        }
        
        const shareUrl = `${currentUrl}?${params.toString()}`;
        console.log('Share URL:', shareUrl); // Debug
        
        // Copy to clipboard with feedback
        copyToClipboard(shareUrl);
        
        // Track sharing
        if (typeof posthog !== 'undefined') {
            posthog.capture('connection_shared', {
                has_code: !!sessionCode,
                instance_domain: instanceUrl.split('.')[0]
            });
        }
    } catch (error) {
        console.error('Share function error:', error);
        showTemporaryMessage('Error creating share link', 'error');
    }
}

// Copy to clipboard with elegant feedback
async function copyToClipboard(text) {
    console.log('Attempting to copy:', text); // Debug
    
    if (!text) {
        console.error('No text provided to copy');
        showTemporaryMessage('Error: No text to copy', 'error');
        return;
    }
    
    try {
        // Try modern clipboard API first
        if (navigator.clipboard && window.isSecureContext) {
            console.log('Using modern clipboard API'); // Debug
            await navigator.clipboard.writeText(text);
            console.log('Modern clipboard API succeeded'); // Debug
            showTemporaryMessage('Share link copied to clipboard!', 'success');
            return;
        }
        
        console.log('Modern clipboard not available, using fallback'); // Debug
        
        // Fallback method for older browsers or non-secure contexts
        const textArea = document.createElement('textarea');
        textArea.value = text;
        textArea.style.position = 'fixed';
        textArea.style.left = '-999999px';
        textArea.style.top = '-999999px';
        textArea.style.opacity = '0';
        textArea.style.pointerEvents = 'none';
        
        document.body.appendChild(textArea);
        textArea.focus();
        textArea.select();
        textArea.setSelectionRange(0, 99999); // For mobile devices
        
        try {
            const success = document.execCommand('copy');
            console.log('Copy command result:', success); // Debug
            
            if (success) {
                showTemporaryMessage('Share link copied to clipboard!', 'success');
            } else {
                // Even if execCommand returns false, show the URL for manual copy
                showTemporaryMessage(`Copy this link: ${text}`, 'info');
                console.log('execCommand returned false, but showing URL for manual copy');
            }
        } catch (err) {
            console.error('execCommand failed:', err);
            showTemporaryMessage(`Copy this link: ${text}`, 'info');
        } finally {
            document.body.removeChild(textArea);
        }
        
    } catch (err) {
        console.error('All copy methods failed:', err);
        showTemporaryMessage(`Copy this link: ${text}`, 'info');
    }
}

// Show temporary message with elegant styling
function showTemporaryMessage(message, type = 'info') {
    console.log('Showing temporary message:', message, type); // Debug
    
    // Remove any existing temporary messages
    const existingMessage = document.querySelector('.temp-message');
    if (existingMessage) {
        existingMessage.remove();
    }
    
    // Create message element
    const messageDiv = document.createElement('div');
    messageDiv.className = 'temp-message fixed top-4 left-1/2 transform -translate-x-1/2 px-6 py-3 rounded-2xl shadow-lg z-50 transition-all duration-300 translate-y-[-20px] opacity-0';
    
    // Style based on type
    switch (type) {
        case 'success':
            messageDiv.className += ' bg-green-500 text-white';
            break;
        case 'warning':
            messageDiv.className += ' bg-yellow-500 text-white';
            break;
        case 'error':
            messageDiv.className += ' bg-red-500 text-white';
            break;
        default:
            messageDiv.className += ' bg-blue-500 text-white';
    }
    
    // Add icon and text
    const icons = {
        success: '✓',
        warning: '⚠',
        error: '✕',
        info: 'ℹ'
    };
    
    messageDiv.innerHTML = `
        <div class="flex items-center space-x-2">
            <span class="text-lg">${icons[type] || icons.info}</span>
            <span class="font-medium">${message}</span>
        </div>
    `;
    
    document.body.appendChild(messageDiv);
    
    // Animate in
    setTimeout(() => {
        messageDiv.classList.remove('translate-y-[-20px]', 'opacity-0');
        messageDiv.classList.add('translate-y-0', 'opacity-100');
    }, 10);
    
    // Auto-hide after 3 seconds
    setTimeout(() => {
        messageDiv.classList.remove('translate-y-0', 'opacity-100');
        messageDiv.classList.add('translate-y-[-20px]', 'opacity-0');
        
        setTimeout(() => {
            if (messageDiv.parentNode) {
                messageDiv.remove();
            }
        }, 300);
    }, 3000);
} 