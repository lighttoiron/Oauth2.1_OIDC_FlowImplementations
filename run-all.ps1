# Run from oauth-lab ./run-all.ps1
# If execution policy is restricted, try running Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
# The above command will allow the running of all .ps1 scripts

# Stop the script execution on error (barring any explicit overrides like in Wait-ForReady)
$ErrorActionPreference = "Stop"

function Wait-ForReady {
    param($Name, $Url)
    $maxAttempts = 30
    $attempt = 0

    Write-Host "Waiting for $Name to be ready..."
    while ($attempt -lt $maxAttempts) {
        try {
            $response = Invoke-WebRequest -Uri $Url `
                -SkipCertificateCheck `
                -TimeoutSec 2 `
                -ErrorAction SilentlyContinue
            if ($response.StatusCode -lt 500) {
                Write-Host "$Name is ready."
                return $true
            }
        } catch {
            # Server is not ready yet, keep polling.
        }
        $attempt++
        Start-Sleep -Seconds 1
    }

    Write-Host "ERROR: $Name failed to start after $maxAttempts seconds"
    return $false
}

# Track processes for cleanup
$processes = @()

function Stop-AllProjects {
    Write-Host "`nShutting down all projects..."
    foreach ($p in $processes) {
        if (!$p.HasExited) {
            $p.Kill()
        }
    }
    Write-Host "All projects stopped."
}

# Register cleanup on Ctrl+C
[Console]::CancelKeyPress += {
    param($sender, $e)
    $e.Cancel = $true # prevent immediate termination so we can clean up
    Stop-AllProjects
    exit 0
}

Write-Host "Starting OAuth Lab..."
Write-Host ""

# Start the Auth Server
$auth = Start-Process "dotnet" `
    -ArgumentList "run", "--launch-profile", "https" `
    -WorkingDirectory "$PSScriptRoot/AuthServer" `
    -PassThru `
    -NoNewWindow
$processes += $auth
Write-Host "AuthServer starting (PID $($auth.Id))..."

if (!(Wait-ForReady "AuthServer" "https://localhost:7010/.well-known/openid-configuration")) {
    Stop-AllProjects
    exit 1
}

# Start ResourceApi
$api = Start-Process "dotnet" `
    -ArgumentList "run", "--launch-profile", "https" `
    -WorkingDirectory "$PSScriptRoot/ResourceApi" `
    -PassThru `
    -NoNewWindow
$processes += $api
Write-Host "ResourceApi starting (PID $(api.Id))..."

# Start Client
$client = Start-Process "dotnet" `
    -ArgumentList "run", "--launch-profile", "https" `
    -WorkingDirectory "$PSScriptRoot/Client" `
    -PassThru `
    -NoNewWindow
    $processes += $client
Write-Host "Client Starting (PID $(client.Id))..."

if (!(Wait-ForReady "Client" "https://localhost:7000")) {
    Stop-AllProjects
    exit 1
}

Write-Host ""
Write-Host "All projects ready."
Write-Host ""
Write-Host: "Client        https://localhost:7000"
Write-Host " Auth Server:  https://localhost:7010"
Write-Host " Resource API: https://localhost:7020"
Write-Host ""
Write-Host "Press Crtl+C to stop all."

# Wait for all processes to exit
$processes | ForEach-Object { $_.WaitForExit() }