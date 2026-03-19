# Block-Update-Windows-Defender

Windows WPF local tool for VPS management:
- Windows Update control (enable/disable + hardening)
- Microsoft Defender control
- Time and time zone sync (auto/manual)
- Browser installer (Chrome, Firefox, Edge, Brave, Opera, CentBrowser)
- Access and security tools (change Windows password, change RDP port, extend C: drive)
- Software update checker/updater
- RDP login IP history (last 30 days)
- Activity logs

## UI Preview

![Windows VPS Control Center](./Block-Update-Windows-Defender.png)

## System Requirements

- Windows Server 2012 R2 or newer
- .NET Framework 4.5.1 or newer
- Run as Administrator for policy/service/security tasks
- Internet is required for:
  - IP/timezone detection
  - browser downloads
  - software update checks

## Prepare VPS Template (Unblock + Exclusion + NGen)

Script file:
- `Prepare-VpsTemplate.ps1`

Run as Administrator:

```powershell
Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process -Force
cd "C:\path\to\Block Update Windows & Defender"
.\Prepare-VpsTemplate.ps1
```

What this script does:
- Unblock all app files (remove MOTW / `Zone.Identifier`)
- Add Microsoft Defender exclusions (app folder + main EXE)
- Run NGen optimization for .NET Framework 4.x

Optional flags:

```powershell
.\Prepare-VpsTemplate.ps1 -SkipUnblock
.\Prepare-VpsTemplate.ps1 -SkipExclusion
.\Prepare-VpsTemplate.ps1 -SkipNGen
.\Prepare-VpsTemplate.ps1 -AppRoot "C:\Program Files\WindowsVpsControlCenter"
```

Recommended after running script:
- reboot template once, then capture your VPS image

## Build

```powershell
dotnet restore
dotnet build
dotnet build -c Release
```

Release output:
- `bin\Release\net451\`

## Packaging

Release ZIP is stored in:
- `release\Block-Update-Windows-Defender-v<version>.zip`

## Logs

Runtime logs:
- `C:\ProgramData\BlockUpdateWindowsDefender\activity.log`
- `C:\ProgramData\BlockUpdateWindowsDefender\startup-error.log`
