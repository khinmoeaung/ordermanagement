<#
.SYNOPSIS
    Regenerates the EF Core model from the SQL schema scripts (database-first).

.DESCRIPTION
    1. Starts a throwaway mysql:8.4 container on port 3307 and waits until it answers.
    2. Applies every database/migrations/*.sql script, in name order.
    3. Runs `dotnet ef dbcontext scaffold` with the customized templates in
       src/OrderManagement.Infrastructure/CodeTemplates/EFCore.
    4. Removes the container, even when a step fails.

    Requires Docker. Run from anywhere: paths are resolved from the repository root.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot      = Split-Path -Parent $PSScriptRoot
$containerName = "ordermanagement-scaffold-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
$hostPort      = 3307
$database      = 'ordermanagement'
$rootPassword  = 'scaffold'
$connection    = "Server=127.0.0.1;Port=$hostPort;Database=$database;Uid=root;Pwd=$rootPassword"
$tables        = @('users', 'customers', 'products', 'orders', 'order_items', 'refresh_tokens')

# Send UTF-8 (no BOM) when piping script text into `docker exec -i`.
$OutputEncoding = New-Object System.Text.UTF8Encoding($false)

# Native tools write progress and warnings to stderr. Under 'Stop', Windows PowerShell 5.1
# turns redirected stderr into a terminating error, so native calls run under 'Continue'
# and are judged by their exit code instead.
function Invoke-Native {
    param([string]$Description, [scriptblock]$Command)
    $ErrorActionPreference = 'Continue'
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Test-MySqlReady {
    $ErrorActionPreference = 'Continue'
    # Ping over TCP: the image's temporary init server listens on the socket only,
    # so a TCP ping succeeds only once the real server is up.
    docker exec -e "MYSQL_PWD=$rootPassword" $containerName `
        mysqladmin ping -h 127.0.0.1 -uroot --silent 2>$null | Out-Null
    return ($LASTEXITCODE -eq 0)
}

Push-Location $repoRoot
try {
    Write-Host "Starting $containerName (mysql:8.4) on port $hostPort..."
    Invoke-Native 'docker run' {
        docker run -d --name $containerName `
            -e "MYSQL_ROOT_PASSWORD=$rootPassword" `
            -e "MYSQL_DATABASE=$database" `
            -p "${hostPort}:3306" `
            mysql:8.4 | Out-Null
    }

    Write-Host 'Waiting for MySQL...'
    $ready = $false
    for ($attempt = 1; $attempt -le 90; $attempt++) {
        if (Test-MySqlReady) { $ready = $true; break }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw 'MySQL did not become ready in time.' }

    $sqlFiles = Get-ChildItem -Path (Join-Path $repoRoot 'database/migrations') -Filter '*.sql' |
        Sort-Object Name
    foreach ($sqlFile in $sqlFiles) {
        Write-Host "Applying $($sqlFile.Name)..."
        Invoke-Native "Applying $($sqlFile.Name)" {
            Get-Content -Raw -Encoding UTF8 $sqlFile.FullName |
                docker exec -i -e "MYSQL_PWD=$rootPassword" $containerName mysql -uroot $database
        }
    }

    $tableArgs = foreach ($table in $tables) { '--table'; $table }

    Write-Host 'Scaffolding the EF Core model...'
    Invoke-Native 'dotnet ef dbcontext scaffold' {
        dotnet tool run dotnet-ef dbcontext scaffold $connection MySql.EntityFrameworkCore `
            -p src/OrderManagement.Infrastructure `
            -s src/OrderManagement.Infrastructure `
            --context AppDbContext `
            --context-dir Persistence `
            --context-namespace OrderManagement.Infrastructure.Persistence `
            --output-dir ../OrderManagement.Domain/Entities `
            --namespace OrderManagement.Domain.Entities `
            --no-onconfiguring `
            --force `
            @tableArgs
    }

    Write-Host 'Scaffold complete.'
}
finally {
    Pop-Location
    Write-Host "Removing $containerName..."
    $ErrorActionPreference = 'Continue'
    docker rm -f $containerName 2>$null | Out-Null
}
