[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$AppRoot = $PSScriptRoot,

    [Parameter(Mandatory = $false)]
    [string]$MainExeName = "BlockUpdateWindowsDefender.exe",

    [Parameter(Mandatory = $false)]
    [switch]$SkipUnblock,

    [Parameter(Mandatory = $false)]
    [switch]$SkipExclusion,

    [Parameter(Mandatory = $false)]
    [switch]$SkipNGen
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Cyan
}

function Write-Ok {
    param([string]$Message)
    Write-Host "[ OK ] $Message" -ForegroundColor Green
}

function Write-WarnMsg {
    param([string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Resolve-AppRoot {
    param([string]$Path)
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    return $resolved.Path
}

function Unblock-AppFiles {
    param([string]$RootPath)

    $files = Get-ChildItem -LiteralPath $RootPath -File -Recurse -Force -ErrorAction Stop
    if (-not $files) {
        Write-WarnMsg "No files found under '$RootPath'."
        return
    }

    $processed = 0
    $zoneRemoved = 0
    foreach ($file in $files) {
        $processed++
        try {
            Unblock-File -LiteralPath $file.FullName -ErrorAction SilentlyContinue
        } catch {
        }

        try {
            $stream = Get-Item -LiteralPath $file.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue
            if ($stream) {
                Remove-Item -LiteralPath $file.FullName -Stream Zone.Identifier -Force -ErrorAction SilentlyContinue
                $zoneRemoved++
            }
        } catch {
        }
    }

    Write-Ok "Unblock completed. Files scanned: $processed, Zone.Identifier removed: $zoneRemoved."
}

function Add-DefenderExclusions {
    param(
        [string]$RootPath,
        [string]$ExePath
    )

    if (-not (Get-Command Get-MpPreference -ErrorAction SilentlyContinue) -or
        -not (Get-Command Add-MpPreference -ErrorAction SilentlyContinue)) {
        Write-WarnMsg "Defender cmdlets are unavailable. Skipping exclusion step."
        return
    }

    try {
        $pref = Get-MpPreference
    } catch {
        Write-WarnMsg "Cannot read Defender preferences: $($_.Exception.Message)"
        return
    }

    $existingPaths = @()
    $existingProcesses = @()
    if ($pref.ExclusionPath) { $existingPaths = @($pref.ExclusionPath) }
    if ($pref.ExclusionProcess) { $existingProcesses = @($pref.ExclusionProcess) }

    if ($existingPaths -notcontains $RootPath) {
        Add-MpPreference -ExclusionPath $RootPath -ErrorAction Stop
        Write-Ok "Added Defender exclusion path: $RootPath"
    } else {
        Write-Info "Defender exclusion path already exists: $RootPath"
    }

    if (Test-Path -LiteralPath $ExePath) {
        if ($existingProcesses -notcontains $ExePath) {
            Add-MpPreference -ExclusionProcess $ExePath -ErrorAction Stop
            Write-Ok "Added Defender exclusion process: $ExePath"
        } else {
            Write-Info "Defender exclusion process already exists: $ExePath"
        }
    } else {
        Write-WarnMsg "Main EXE not found for process exclusion: $ExePath"
    }
}

function Run-NGenOptimization {
    param([string]$ExePath)

    if (-not (Test-Path -LiteralPath $ExePath)) {
        Write-WarnMsg "Main EXE not found. Skipping NGen: $ExePath"
        return
    }

    $ngenCandidates = @(
        (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\ngen.exe"),
        (Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\ngen.exe")
    )

    $ran = $false
    foreach ($ngen in $ngenCandidates) {
        if (-not (Test-Path -LiteralPath $ngen)) {
            continue
        }

        $ran = $true
        Write-Info "Running NGen install with: $ngen"
        & $ngen install $ExePath /nologo /queue:1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-WarnMsg "NGen install exited with code ${LASTEXITCODE}: $ngen"
        } else {
            Write-Ok "NGen install queued successfully: $ngen"
        }

        Write-Info "Executing NGen queue with: $ngen"
        & $ngen executeQueuedItems /nologo | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-WarnMsg "NGen executeQueuedItems exited with code ${LASTEXITCODE}: $ngen"
        } else {
            Write-Ok "NGen queue executed successfully: $ngen"
        }
    }

    if (-not $ran) {
        Write-WarnMsg "No NGen executable found for .NET Framework v4.0.30319."
    }
}

try {
    if (-not (Test-Administrator)) {
        throw "Please run this script as Administrator."
    }

    $resolvedRoot = Resolve-AppRoot -Path $AppRoot
    $mainExePath = Join-Path $resolvedRoot $MainExeName

    Write-Info "App root: $resolvedRoot"
    Write-Info "Main EXE: $mainExePath"

    if (-not $SkipUnblock) {
        Write-Info "Step 1/3: Unblock files (remove MOTW)..."
        Unblock-AppFiles -RootPath $resolvedRoot
    } else {
        Write-Info "Step 1/3 skipped: Unblock"
    }

    if (-not $SkipExclusion) {
        Write-Info "Step 2/3: Add Defender exclusions..."
        Add-DefenderExclusions -RootPath $resolvedRoot -ExePath $mainExePath
    } else {
        Write-Info "Step 2/3 skipped: Defender exclusion"
    }

    if (-not $SkipNGen) {
        Write-Info "Step 3/3: Run NGen optimization..."
        Run-NGenOptimization -ExePath $mainExePath
    } else {
        Write-Info "Step 3/3 skipped: NGen"
    }

    Write-Host ""
    Write-Ok "Template preparation completed."
    Write-Info "Recommended: reboot template once after running this script."
    exit 0
}
catch {
    Write-Host ""
    Write-Host "[FAIL] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
