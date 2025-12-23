Write-Host "🚀 Starting OTel Verification Traffic Generation..." -ForegroundColor Green

$baseUrl = "http://localhost:5000"

# Function to make requests
function Invoke-Request {
    param (
        [string]$Method,
        [string]$Path,
        [string]$Body = $null
    )
    
    try {
        $uri = "$baseUrl$Path"
        Write-Host "👉 $Method $uri" -NoNewline
        
        if ($Body) {
            $response = Invoke-RestMethod -Uri $uri -Method $Method -Body $Body -ContentType "application/json" -ErrorAction Stop
        } else {
            $response = Invoke-RestMethod -Uri $uri -Method $Method -ErrorAction Stop
        }
        
        Write-Host " [OK]" -ForegroundColor Green
        return $response
    }
    catch {
        Write-Host " [ERROR] $($_.Exception.Message)" -ForegroundColor Red
    }
}

# 1. Basic Health Check
Invoke-Request -Method "GET" -Path "/health"

# 2. Set some cache values (PUT/POST)
# Assuming there is a generic cache controller or we use the Product example if available.
# Checking typical example endpoints based on previous context or common patterns.
# Let's try the generic /api/cache endpoint mentioned in docs.

$key = "otel-test-key-$(Get-Random)"
$value = "otel-test-value-$(Get-Date)"

# 3. Write Cache
Invoke-Request -Method "POST" -Path "/api/Basics/$key?ttlSeconds=600" -Body "`"$value`""

# 4. Read Cache (Hit)
Invoke-Request -Method "GET" -Path "/api/Basics/$key"

# 5. Read Cache (Miss)
$missingKey = "missing-key-$(Get-Random)"
Invoke-Request -Method "GET" -Path "/api/Basics/$missingKey"

# 6. Simulate some load
Write-Host "Running mini-load test..." -ForegroundColor Yellow
for ($i = 1; $i -le 10; $i++) {
    $k = "load-key-$i"
    Invoke-Request -Method "POST" -Path "/api/Basics/$k?ttlSeconds=60" -Body "`"val-$i`"" | Out-Null
    Invoke-Request -Method "GET" -Path "/api/Basics/$k" | Out-Null
}

Write-Host "✅ Verification traffic sent." -ForegroundColor Green
Write-Host "Check OpenObserve at http://localhost:5080 (admin@example.com / admin123)" -ForegroundColor White
