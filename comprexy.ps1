# Local DX entrypoint for Comprexy (proxy + control-api) on Windows.
# Prefer: .\comprexy.cmd <command>   (works from cmd.exe / PowerShell)
# Or:     .\comprexy.ps1 <command>   (may require ExecutionPolicy)

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Command = "help",

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CommandArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ($null -eq $CommandArgs) {
    $CommandArgs = @()
}

$Root = $PSScriptRoot
Set-Location $Root

$ProxyProject = "apps/proxy/Comprexy.Api.csproj"
$ControlProject = "apps/control-api/Comprexy.ControlApi.csproj"
$DotnetChannel = "10.0"
$DotnetInstallDir = if ($env:DOTNET_ROOT) { $env:DOTNET_ROOT } else { Join-Path $env:USERPROFILE ".dotnet" }
$DotnetInstallScriptUrl = "https://dot.net/v1/dotnet-install.ps1"

function Show-Usage {
    @"
Usage: .\comprexy.cmd <command> [args...]
       .\comprexy.ps1 <command> [args...]

Commands:
  proxy [args...]       Run data-plane proxy (:8129)
  control-api [args...] Run control-api metrics host (:8130)
  control [args...]     Alias for control-api
  dev                   Run proxy + control-api together (Ctrl-C stops both)
  test [args...]        Run solution tests
  build [args...]       Build the solution
  clear-db              Rebuild SQLite from migrations (proxy --clear-db)
  install-dotnet        Install .NET 10 SDK into %USERPROFILE%\.dotnet (official script)
  help                  Show this help

If .NET 10 is missing, run/build commands offer to install it (interactive),
or set COMPREXY_AUTO_INSTALL_DOTNET=1 to install without prompting.

Examples:
  .\comprexy.cmd proxy
  .\comprexy.cmd control-api
  .\comprexy.cmd dev
  .\comprexy.cmd test
  .\comprexy.cmd clear-db
  .\comprexy.cmd install-dotnet
"@
}

function Prefer-LocalDotnet {
    $dotnetExe = Join-Path $DotnetInstallDir "dotnet.exe"
    if (Test-Path -LiteralPath $dotnetExe) {
        $env:DOTNET_ROOT = $DotnetInstallDir
        $env:PATH = "$DotnetInstallDir;$DotnetInstallDir\tools;$env:PATH"
    }
}

function Test-DotnetSdk10 {
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        return $false
    }
    $sdks = & dotnet --list-sdks 2>$null
    if (-not $sdks) {
        return $false
    }
    foreach ($line in $sdks) {
        $version = ($line -split '\s+')[0]
        $major = [int](($version -split '\.')[0])
        if ($major -ge 10) {
            return $true
        }
    }
    return $false
}

function Install-DotnetSdk {
    Write-Host "Installing .NET SDK $DotnetChannel into $DotnetInstallDir…"
    Write-Host "(official script: $DotnetInstallScriptUrl)"
    Write-Host

    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("comprexy-dotnet-install-{0}.ps1" -f [guid]::NewGuid().ToString("n"))
    try {
        Invoke-WebRequest -Uri $DotnetInstallScriptUrl -OutFile $tmp -UseBasicParsing
        & $tmp -Channel $DotnetChannel -InstallDir $DotnetInstallDir
    }
    finally {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }

    Prefer-LocalDotnet

    if (-not (Test-DotnetSdk10)) {
        throw "error: .NET SDK $DotnetChannel install finished but SDK 10+ was not detected."
    }

    $ver = & dotnet --version
    Write-Host
    Write-Host ".NET SDK ready: $ver"
    Write-Host
    Write-Host "Add to your user PATH for new terminals (PowerShell):"
    Write-Host "  [Environment]::SetEnvironmentVariable('DOTNET_ROOT', '$DotnetInstallDir', 'User')"
    Write-Host "  # then prepend `$env:DOTNET_ROOT and `$env:DOTNET_ROOT\tools to User PATH"
}

function Offer-DotnetInstall {
    param([string]$Reason)

    Write-Host $Reason
    Write-Host

    if ($env:COMPREXY_AUTO_INSTALL_DOTNET -eq "1") {
        Write-Host "COMPREXY_AUTO_INSTALL_DOTNET=1 → installing .NET $DotnetChannel…"
        Install-DotnetSdk
        return
    }

    $inputRedirected = $false
    try {
        $inputRedirected = [Console]::IsInputRedirected
    }
    catch {
        $inputRedirected = $false
    }

    if (-not [Environment]::UserInteractive -or $inputRedirected) {
        Write-Host @"
Non-interactive shell: install with:
  .\comprexy.cmd install-dotnet
or:
  `$env:COMPREXY_AUTO_INSTALL_DOTNET=1; .\comprexy.cmd <command>

Manual download: https://dotnet.microsoft.com/download/dotnet/10.0
"@
        exit 1
    }

    $answer = Read-Host "Install .NET $DotnetChannel SDK into $DotnetInstallDir now? [y/N]"
    switch -Regex ($answer) {
        '^(y|yes)$' {
            Install-DotnetSdk
        }
        default {
            Write-Host @"
Aborted. Install later with:
  .\comprexy.cmd install-dotnet
or: https://dotnet.microsoft.com/download/dotnet/10.0
"@
            exit 1
        }
    }
}

function Require-Dotnet {
    Prefer-LocalDotnet

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Offer-DotnetInstall "error: .NET SDK not found on PATH (need .NET $DotnetChannel+)."
        Prefer-LocalDotnet
    }

    $sdks = & dotnet --list-sdks 2>$null
    if (-not $sdks) {
        $dotnetPath = (Get-Command dotnet).Source
        Offer-DotnetInstall "error: ``dotnet`` is on PATH ($dotnetPath) but no SDKs are installed."
        Prefer-LocalDotnet
        $sdks = & dotnet --list-sdks 2>$null
    }

    if (-not (Test-DotnetSdk10)) {
        $found = ($sdks | Out-String).TrimEnd()
        Offer-DotnetInstall "error: .NET $DotnetChannel+ SDK is required (found only):`n$found"
        Prefer-LocalDotnet
    }

    if (-not (Test-DotnetSdk10)) {
        throw "error: .NET $DotnetChannel+ SDK still not available after install attempt."
    }
}

function Invoke-Proxy {
    Require-Dotnet
    & dotnet run --project $ProxyProject -- @CommandArgs
    exit $LASTEXITCODE
}

function Invoke-Control {
    Require-Dotnet
    & dotnet run --project $ControlProject -- @CommandArgs
    exit $LASTEXITCODE
}

function Invoke-Dev {
    Require-Dotnet

    $proxyProc = $null
    $controlProc = $null

    Write-Host "Starting proxy (:8129) and control-api (:8130)…"
    Write-Host "Press Ctrl-C to stop both."
    Write-Host

    try {
        $proxyProc = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", $ProxyProject) -WorkingDirectory $Root -NoNewWindow -PassThru
        $controlProc = Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", $ControlProject) -WorkingDirectory $Root -NoNewWindow -PassThru
        Wait-Process -InputObject @($proxyProc, $controlProc)
    }
    finally {
        foreach ($proc in @($proxyProc, $controlProc)) {
            if ($null -ne $proc -and -not $proc.HasExited) {
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

function Invoke-Test {
    Require-Dotnet
    & dotnet test (Join-Path $Root "Comprexy.slnx") @CommandArgs
    exit $LASTEXITCODE
}

function Invoke-Build {
    Require-Dotnet
    & dotnet build (Join-Path $Root "Comprexy.slnx") @CommandArgs
    exit $LASTEXITCODE
}

function Invoke-ClearDb {
    Require-Dotnet
    Write-Host "Rebuilding database (data/comprexy.db) from migrations…"
    & dotnet run --project $ProxyProject -- --clear-db
    exit $LASTEXITCODE
}

switch -Regex ($Command) {
    '^(proxy)$' {
        Invoke-Proxy
    }
    '^(control-api|control)$' {
        Invoke-Control
    }
    '^(dev)$' {
        Invoke-Dev
    }
    '^(test)$' {
        Invoke-Test
    }
    '^(build)$' {
        Invoke-Build
    }
    '^(clear-db)$' {
        Invoke-ClearDb
    }
    '^(install-dotnet)$' {
        Install-DotnetSdk
    }
    '^(help|-h|--help)$' {
        Show-Usage
    }
    default {
        Write-Host "error: unknown command '$Command'" -ForegroundColor Red
        Write-Host
        Show-Usage
        exit 1
    }
}
