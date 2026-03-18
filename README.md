# Block-Update-Windows-Defender

Local WPF tool for a single Windows VPS.

Current MVP features:

- Detect current Windows version automatically
- Enable or disable Automatic Windows Update by local policy
- Enable or disable Microsoft Defender real-time protection when supported
- Auto sync time and time zone on startup
- Manual sync time and time zone with one button
- Open log folder from the UI
- Run the app automatically when Windows starts

Notes:

- The app runs as administrator via `app.manifest`
- Time zone auto-detection uses `https://ipapi.co/timezone/`
- On Windows Server 2012 R2, Defender can show as unsupported depending on installed components
- If Tamper Protection is enabled, Defender changes can be blocked by Windows

Release output:

- Debug build: `bin\Debug\net48\`
- Release build: `bin\Release\net48\`
