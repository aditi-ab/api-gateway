[CmdletBinding()]
param([switch]$KeepRunning)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$verificationPassword = 'ApiGateway verification password 42!'

function Wait-Http {
    param([string]$Uri, [int]$Attempts = 60)
    for ($attempt = 0; $attempt -lt $Attempts; $attempt++) {
        try { return Invoke-RestMethod -Uri $Uri -TimeoutSec 3 }
        catch { Start-Sleep -Seconds 2 }
    }
    throw "Timed out waiting for $Uri."
}

function Invoke-GraphQl {
    param([string]$Query, [hashtable]$Variables = @{})
    $body = @{ query = $Query; variables = $Variables } | ConvertTo-Json -Depth 30 -Compress
    $response = Invoke-RestMethod -Uri 'http://localhost:5080/graphql' -Method Post -WebSession $script:session -Headers @{ 'X-CSRF-TOKEN' = $script:csrf } -ContentType 'application/json' -Body $body
    if ($response.errors) { throw ($response.errors | ConvertTo-Json -Depth 20) }
    return $response.data
}

Push-Location $root
$previousComposeProject = $env:COMPOSE_PROJECT_NAME
$previousSqlPassword = $env:MSSQL_SA_PASSWORD
try {
    $env:COMPOSE_PROJECT_NAME = 'apigateway-verification'
    $env:MSSQL_SA_PASSWORD = $verificationPassword
    docker compose down --volumes --remove-orphans | Out-Null
    docker compose up --build --detach --wait
    if ($LASTEXITCODE -ne 0) { throw 'Docker Compose did not start successfully.' }

    $script:session = [Microsoft.PowerShell.Commands.WebRequestSession]::new()
    Wait-Http 'http://localhost:5080/readyz' | Out-Null
    $status = Invoke-RestMethod -Uri 'http://localhost:5080/admin/auth/status' -WebSession $script:session
    $script:csrf = $status.antiforgeryToken
    if ($status.bootstrapRequired) {
        $credentials = @{ username = 'compose.admin'; password = 'Compose verification password 42!' } | ConvertTo-Json
        Invoke-RestMethod -Uri 'http://localhost:5080/admin/auth/bootstrap' -Method Post -WebSession $script:session -Headers @{ 'X-CSRF-TOKEN' = $script:csrf } -ContentType 'application/json' -Body $credentials
        $status = Invoke-RestMethod -Uri 'http://localhost:5080/admin/auth/status' -WebSession $script:session
        $script:csrf = $status.antiforgeryToken
    }

    $environment = (Invoke-GraphQl 'mutation { createEnvironment(slug:"development", displayName:"Compose verification") { id } }').createEnvironment
    $draft = (Invoke-GraphQl 'mutation($environmentId:UUID!){createDraft(environmentId:$environmentId){revision{id concurrencyVersion}}}' @{ environmentId = $environment.id }).createDraft.revision
    $configuration = @{
        schemaVersion = 1
        routes = @(@{ id = 'compose-smoke'; match = @{ path = '/{**remainder}' }; clusterId = 'upstream' })
        clusters = @(@{ id = 'upstream'; destinations = @{ primary = @{ address = 'http://upstream:80/' } } })
        policies = @{}
    } | ConvertTo-Json -Depth 20 -Compress
    $draft = (Invoke-GraphQl 'mutation($id:UUID!,$version:UUID!,$json:String!){setDraftContent(draftId:$id,expectedVersion:$version,json:$json){revision{id concurrencyVersion}}}' @{ id = $draft.id; version = $draft.concurrencyVersion; json = $configuration }).setDraftContent.revision
    $published = (Invoke-GraphQl 'mutation($id:UUID!,$version:UUID!){publishDraft(draftId:$id,expectedVersion:$version){id}}' @{ id = $draft.id; version = $draft.concurrencyVersion }).publishDraft

    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        try {
            $one = Invoke-RestMethod -Uri 'http://localhost:5070/readyz'
            $two = Invoke-RestMethod -Uri 'http://localhost:5071/readyz'
            if ($one.revisionId -eq $published.id -and $two.revisionId -eq $published.id) { $ready = $true; break }
        } catch { }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw 'Both ApiGateway instances did not activate the published revision.' }

    $firstResponse = Invoke-WebRequest -UseBasicParsing 'http://localhost:5070/'
    $secondResponse = Invoke-WebRequest -UseBasicParsing 'http://localhost:5071/'
    if ($firstResponse.StatusCode -ne 200 -or $secondResponse.StatusCode -ne 200) { throw 'Proxy verification failed.' }
    Write-Host "Verified revision $($published.id) on both SQL Server-backed ApiGateway instances."
}
finally {
    if (-not $KeepRunning) { docker compose down --volumes }
    if ($null -eq $previousComposeProject) { Remove-Item Env:COMPOSE_PROJECT_NAME -ErrorAction SilentlyContinue } else { $env:COMPOSE_PROJECT_NAME = $previousComposeProject }
    if ($null -eq $previousSqlPassword) { Remove-Item Env:MSSQL_SA_PASSWORD -ErrorAction SilentlyContinue } else { $env:MSSQL_SA_PASSWORD = $previousSqlPassword }
    Pop-Location
}
