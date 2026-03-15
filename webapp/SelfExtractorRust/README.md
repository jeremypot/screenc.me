# ScreenConnect Rust Self-Extractor

A lightweight, fast self-extracting executable for ScreenConnect session files.

## Features

- ✅ **Small size**: ~1-2MB (vs 10MB+ .NET version)
- ✅ **No dependencies**: Runs on any Windows x64 machine
- ✅ **Fast**: Native Rust performance
- ✅ **Memory safe**: Rust's memory safety guarantees
- ✅ **Cross-platform build**: Builds on Linux for Windows targets

## How it works

1. User downloads `ScreenConnect_[sessionId].exe`
2. User runs the executable
3. Shows prompt: "Extract and run ScreenConnect session [sessionId]?"
4. Extracts ZIP data appended to the executable
5. Creates `ScreenConnect_[sessionId]/` folder
6. Launches `ScreenConnect.Client.exe` automatically

## Build

```bash
# Cross-compile for Windows from Linux
cargo build --release --target x86_64-pc-windows-gnu

# Output: target/x86_64-pc-windows-gnu/release/screenconnect-extractor.exe
```

## Dependencies

- `zip = "0.6"` - ZIP file handling

## Size optimization

The `Cargo.toml` includes aggressive size optimizations:
- `strip = true` - Remove debug symbols
- `lto = true` - Link-time optimization  
- `opt-level = "z"` - Optimize for size
- `panic = "abort"` - Remove panic unwinding code

## Usage

The webapp builds this extractor during Docker build and copies `screenconnect-extractor.exe` into the image. When creating a self-extracting package from a ZIP, the service uses this executable as the wrapper. 