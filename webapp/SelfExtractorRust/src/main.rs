use std::env;
use std::fs::{self, File};
use std::io::{self, Read, Write};
use std::path::{Path, PathBuf};
use std::process::Command;
use std::thread;
use std::time::Duration;
use zip::ZipArchive;

fn main() {
    if let Err(e) = run() {
        eprintln!("Error: {}", e);
        // Don't wait for input in case of error - just exit
        std::process::exit(1);
    }
}

fn run() -> Result<(), Box<dyn std::error::Error>> {
    // Get the executable path
    let exe_path = env::current_exe()?;

    // Extract session ID from filename
    let session_id = get_session_id_from_filename(&exe_path);
    let extract_dir = format!("ScreenConnect_{}", session_id);

    // Silent extraction - no user prompt
    // Read ZIP data appended to this EXE
    let zip_data = read_appended_zip_data(&exe_path)?;
    
    // Extract to subfolder in current directory
    let current_dir = env::current_dir()?;
    let full_extract_path = current_dir.join(&extract_dir);
    
    if full_extract_path.exists() {
        fs::remove_dir_all(&full_extract_path)?;
    }
    fs::create_dir_all(&full_extract_path)?;
    
    // Extract ZIP
    extract_zip(&zip_data, &full_extract_path)?;

    // Find and launch ScreenConnect executable
    if let Some(sc_exe) = find_screenconnect_exe(&full_extract_path)? {
        eprintln!("Found ScreenConnect exe: {:?}", sc_exe);
        
        #[cfg(windows)]
        {
            // Try different launch approaches
            eprintln!("Attempting to launch ScreenConnect...");
            
            // Method 1: Direct execution with full path
            match Command::new(&sc_exe)
                .current_dir(&full_extract_path)
                .spawn() 
            {
                Ok(mut child) => {
                    eprintln!("Process started with PID: {:?}", child.id());
                    
                    // Give it a moment to start
                    thread::sleep(Duration::from_millis(500));
                    
                    match child.try_wait() {
                        Ok(Some(status)) => {
                            eprintln!("ScreenConnect exited immediately with status: {}", status);
                            if !status.success() {
                                eprintln!("Exit code indicates failure");
                                
                                // Try method 2: Using cmd.exe to launch
                                eprintln!("Trying alternative launch method...");
                                match Command::new("cmd")
                                    .args(&["/C", "start", "", &sc_exe.to_string_lossy()])
                                    .current_dir(&full_extract_path)
                                    .spawn()
                                {
                                    Ok(_) => eprintln!("Alternative launch method attempted"),
                                    Err(e) => eprintln!("Alternative launch failed: {}", e),
                                }
                            }
                        }
                        Ok(None) => {
                            eprintln!("ScreenConnect is running successfully!");
                        }
                        Err(e) => {
                            eprintln!("Failed to check ScreenConnect status: {}", e);
                        }
                    }
                }
                Err(e) => {
                    eprintln!("Failed to launch ScreenConnect directly: {}", e);
                    eprintln!("Error details: {:?}", e.kind());
                    
                    // Try method 2: Using Windows shell execute
                    eprintln!("Trying shell execute method...");
                    match Command::new("cmd")
                        .args(&["/C", &sc_exe.to_string_lossy()])
                        .current_dir(&full_extract_path)
                        .spawn()
                    {
                        Ok(_) => eprintln!("Shell execute method attempted"),
                        Err(e2) => {
                            eprintln!("Shell execute also failed: {}", e2);
                            eprintln!("Please manually run: {:?}", sc_exe);
                            return Err(e.into());
                        }
                    }
                }
            }
        }
        
        #[cfg(not(windows))]
        {
            eprintln!("Note: Auto-launch only supported on Windows");
        }
    } else {
        eprintln!("ScreenConnect executable not found in extracted files.");
        eprintln!("Contents of extract directory:");
        if let Ok(entries) = std::fs::read_dir(&full_extract_path) {
            for entry in entries.flatten() {
                eprintln!("  {:?}", entry.file_name());
            }
        }
        return Err("ScreenConnect executable not found".into());
    }

    Ok(())
}

fn get_session_id_from_filename(path: &Path) -> String {
    path.file_stem()
        .and_then(|s| s.to_str())
        .and_then(|filename| {
            if filename.starts_with("ScreenConnect_") {
                Some(filename.strip_prefix("ScreenConnect_").unwrap_or("unknown"))
            } else {
                None
            }
        })
        .unwrap_or("unknown")
        .to_string()
}

fn read_appended_zip_data(exe_path: &Path) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
    let all_bytes = fs::read(exe_path)?;
    
    // ZIP signatures
    const END_SIGNATURE: [u8; 4] = [0x50, 0x4B, 0x05, 0x06]; // ZIP End of Central Directory
    const LOCAL_FILE_SIGNATURE: [u8; 4] = [0x50, 0x4B, 0x03, 0x04]; // Local file header
    
    // Look for ZIP Central Directory End signature - most reliable
    let search_start = if all_bytes.len() > 65558 { all_bytes.len() - 65558 } else { 0 };
    
    for i in (search_start..all_bytes.len().saturating_sub(21)).rev() {
        if all_bytes[i..i+4] == END_SIGNATURE {
            // Read central directory info from EOCD
            let central_dir_offset = u32::from_le_bytes([
                all_bytes[i + 16], all_bytes[i + 17], 
                all_bytes[i + 18], all_bytes[i + 19]
            ]) as usize;
            
            let central_dir_size = u32::from_le_bytes([
                all_bytes[i + 12], all_bytes[i + 13], 
                all_bytes[i + 14], all_bytes[i + 15]
            ]) as usize;
            
            // Search for first Local File Header before central directory
            let search_end = std::cmp::min(central_dir_offset, all_bytes.len());
            let search_start = all_bytes.len() / 2;
            
            for j in search_start..search_end {
                if j + 4 <= all_bytes.len() && all_bytes[j..j+4] == LOCAL_FILE_SIGNATURE {
                    let zip_start_offset = j;
                    let zip_data = all_bytes[zip_start_offset..].to_vec();
                    
                    // Validate ZIP
                    validate_zip(&zip_data)?;
                    return Ok(zip_data);
                }
            }
        }
    }
    
    // Fallback: Look for Local File Header signature from back
    let search_start = all_bytes.len() / 2;
    
    for i in search_start..all_bytes.len().saturating_sub(30) {
        if all_bytes[i..i+4] == LOCAL_FILE_SIGNATURE {
            let zip_data = all_bytes[i..].to_vec();
            
            // Validate ZIP
            validate_zip(&zip_data)?;
            return Ok(zip_data);
        }
    }
    
    Err("No ZIP data found in executable".into())
}

fn validate_zip(zip_data: &[u8]) -> Result<(), Box<dyn std::error::Error>> {
    let cursor = std::io::Cursor::new(zip_data);
    let archive = ZipArchive::new(cursor)?;
    
    if archive.len() == 0 {
        return Err("ZIP file is empty".into());
    }
    
    Ok(())
}

fn extract_zip(zip_data: &[u8], extract_path: &Path) -> Result<(), Box<dyn std::error::Error>> {
    let cursor = std::io::Cursor::new(zip_data);
    let mut archive = ZipArchive::new(cursor)?;
    
    for i in 0..archive.len() {
        let mut file = archive.by_index(i)?;
        let file_path = extract_path.join(file.name());
        
        if file.name().ends_with('/') {
            // Directory
            fs::create_dir_all(&file_path)?;
        } else {
            // File
            if let Some(parent) = file_path.parent() {
                fs::create_dir_all(parent)?;
            }
            
            let mut output_file = File::create(&file_path)?;
            io::copy(&mut file, &mut output_file)?;
        }
    }
    
    Ok(())
}

fn find_screenconnect_exe(dir: &Path) -> Result<Option<PathBuf>, Box<dyn std::error::Error>> {
    fn search_dir(dir: &Path) -> Result<Option<PathBuf>, Box<dyn std::error::Error>> {
        for entry in fs::read_dir(dir)? {
            let entry = entry?;
            let path = entry.path();
            
            if path.is_file() {
                if let Some(name) = path.file_name().and_then(|n| n.to_str()) {
                    if name.to_lowercase().contains("screenconnect") && name.to_lowercase().ends_with(".exe") {
                        return Ok(Some(path));
                    }
                }
            } else if path.is_dir() {
                if let Some(found) = search_dir(&path)? {
                    return Ok(Some(found));
                }
            }
        }
        Ok(None)
    }
    
    search_dir(dir)
} 