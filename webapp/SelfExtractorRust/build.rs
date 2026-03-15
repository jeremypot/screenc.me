fn main() {
    // Only include manifest on Windows
    #[cfg(windows)]
    {
        // Embed Windows manifest to avoid UAC prompts
        println!("cargo:rerun-if-changed=manifest.xml");
        
        // Tell Rust to link the manifest
        println!("cargo:rustc-link-arg-bins=/MANIFEST:EMBED");
        println!("cargo:rustc-link-arg-bins=/MANIFESTINPUT:manifest.xml");
        
        // Alternative method using winres crate
        let mut res = winres::WindowsResource::new();
        res.set_manifest_file("manifest.xml");
        res.compile().unwrap();
    }
} 