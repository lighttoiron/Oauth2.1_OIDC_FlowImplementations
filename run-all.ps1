# Run from oauth-lab ./run-all.ps1
# If execution policy is restricted, try running Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
# The above command will allow the running of all .ps1 scripts

# Load the System.Net.Http assembly
Add-Type -AssemblyName System.Net.Http

# If we have not done so already, create a custom HttpClientHandler to allow this script to ignore Https certificate checks
if (-not ([System.Management.Automation.PSTypeName]'InsecureHandler').Type) {
    Add-Type -ReferencedAssemblies 'System.Net.Http' @"
        using System.Net.Http;
        using System.Security.Cryptography.X509Certificates;
        using System.Net.Security;

        public class InsecureHandler {
            public static HttpClientHandler Create() {
                var handler = new HttpClientHandler();
                handler.ServerCertificateCustomValidationCallback =
                    (message, cert, chain, errors) => true;
                return handler;
            }
        }
"@
}

# Waits until the given server responds, which we use to ensure that the Auth server is ready before we start the API server
function Wait-ForReady {
    param($Name, $Url)
    $maxAttempts = 30
    $attempt = 0

    Write-Host "Waiting for $Name to be ready..."
    while ($attempt -lt $maxAttempts) {
        $httpClient = $null
        $handler = $null
        try {
            $handler = [InsecureHandler]::Create()
            $httpClient = New-Object System.Net.Http.HttpClient($handler)
            $httpClient.Timeout = [System.TimeSpan]::FromSeconds(2)

            $response = $httpClient.GetAsync($Url).GetAwaiter().GetResult()
        
            # When we hear back from the server, return true
            if ([int]$response.StatusCode -lt 500) {
                Write-Host "$Name is ready."
                return $true
            }
        } catch {
            # Server is not ready yet, keep polling.
            # Also unwrap the messages for more accurate error messages
            $ex = $_.Exception
            while ($ex.InnerException) { $ex = $ex.InnerException }
            Write-Host "Polling $Name at $Url attempt $attempt failed: $($ex.Message)"
        } finally {
            if ($httpClient) { $httpClient.Dispose() }
            if ($handler) { $handler.Dispose() }
        }
        $attempt++
        Start-Sleep -Seconds 1
    }

    Write-Host "ERROR: $Name failed to start after $maxAttempts seconds"
    return $false
}

# Track processes for cleanup
$processes = @()

# Used to shut down all of the servers when this script is stopped
function Stop-AllProjects {
    Write-Host "`nShutting down all projects..."
    foreach ($p in $script:processes) {
        if (!$p.HasExited) {
            $p.Kill()
        }
    }
    Write-Host "All projects stopped."
}

# Register cleanup on Ctrl+C
Register-ObjectEvent -InputObject ([Console]) `
    -EventName CancelKeyPress `
    -Action {
        $Event.SourceEventArgs.Cancel = $true
        Stop-AllProjects
        exit 0
    } | Out-Null

# Stop the script execution on error (barring any explicit overrides like in Wait-ForReady)
$ErrorActionPreference = "Stop"

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
Write-Host "ResourceApi starting (PID $($api.Id))..."

# Start Client
$client = Start-Process "dotnet" `
    -ArgumentList "run", "--launch-profile", "https" `
    -WorkingDirectory "$PSScriptRoot/Client" `
    -PassThru `
    -NoNewWindow
$processes += $client
Write-Host "Client Starting (PID $($client.Id))..."

# Wait for the client server to respond
if (!(Wait-ForReady "Client" "https://localhost:7000")) {
    Stop-AllProjects
    exit 1
}

Write-Host ""
Write-Host "All projects ready."
Write-Host ""
Write-Host " Client        https://localhost:7000"
Write-Host " Auth Server:  https://localhost:7010"
Write-Host " Resource API: https://localhost:7020"
Write-Host ""
Write-Host "Press Crtl+C to stop all."

# Wait for all processes to exit
$processes | ForEach-Object { $_.WaitForExit() }