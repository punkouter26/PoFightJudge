#Requires -Version 7.0
<#
.SYNOPSIS
    Copies the secrets this solution needs into kv-poshared under PoMarriedFight--*, and adds the site's redirect
    URI to the Entra app registration it signs in with.

.DESCRIPTION
    PoMarriedFight is a merge of two apps that already have their secrets in kv-poshared, so nothing here is new
    material — it is the same values under this solution's prefix, which is what the host reads
    (PoMarriedFight--GeminiApiKey becomes PoMarriedFight:GeminiApiKey in configuration).

    THIS SCRIPT DOES NOTHING UNTIL YOU PASS -Apply. Run it once to read the plan, then again to carry it out.

    Values are never printed, written to disk, or passed on a command line that a process list could show: each one
    is read into a temporary file with `az keyvault secret download` and set with `--file`. Only names and
    decisions appear on screen.

    An existing PoMarriedFight--* secret is left alone unless -Force is given: overwriting a key somebody has
    already rotated is not a copy, it is a regression.

.PARAMETER Apply
    Actually write. Without it the script reads the vault and prints what it would do.

.PARAMETER Force
    Overwrite PoMarriedFight--* secrets that already exist.

.PARAMETER SkipRedirectUri
    Do not touch the Entra app registration.

.PARAMETER Vault
    Key Vault name. Defaults to the shared vault every Po* app uses.

.EXAMPLE
    ./SCRIPTS/seed-secrets.ps1            # read-only: prints the plan
    ./SCRIPTS/seed-secrets.ps1 -Apply     # carries it out
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [switch]$Force,
    [switch]$SkipRedirectUri,
    [string]$Vault = 'kv-poshared'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$SiteUrl = 'https://app-pomarriedfight.azurewebsites.net'
$RedirectUri = "$SiteUrl/authentication/login-callback"

# Verified against kv-poshared on 2026-09-07: these source names exist. The first source that is present wins, so
# the Gemini key comes from PoArgueJudge (the same key both apps use) and falls back to PoMarriedLife.
$Plan = @(
    @{ Target = 'PoMarriedFight--GeminiApiKey';        Sources = @('PoArgueJudge--GeminiApiKey', 'PoMarriedLife--GeminiApiKey'); Required = $true }
    @{ Target = 'PoMarriedFight--AzureAd--TenantId';   Sources = @('PoArgueJudge--AzureAd--TenantId', 'PoMarriedLife--AzureAd--TenantId'); Required = $true }
    @{ Target = 'PoMarriedFight--AzureAd--ClientId';   Sources = @('PoArgueJudge--AzureAd--ClientId', 'PoMarriedLife--AzureAd--ClientId'); Required = $true }
    @{ Target = 'PoMarriedFight--FishAudioApiKey';     Sources = @('PoMarriedLife--FishAudioApiKey'); Required = $false }
    @{ Target = 'PoMarriedFight--AzureSpeechKey';      Sources = @('PoMarriedLife--AzureSpeechKey'); Required = $false }
    @{ Target = 'PoMarriedFight--AzureSpeechRegion';   Sources = @('PoMarriedLife--AzureSpeechRegion'); Required = $false }
)

function Write-Plan($Message) { Write-Host "  $Message" -ForegroundColor Gray }
function Write-Ok($Message) { Write-Host "OK: $Message" -ForegroundColor Green }
function Write-Skipped($Message) { Write-Host "--: $Message" -ForegroundColor DarkGray }
function Write-Todo($Message) { Write-Host "TODO: $Message" -ForegroundColor Yellow }

$account = az account show --query user.name -o tsv 2>$null
if (-not $account) { throw 'Not signed in to Azure. Run az login first.' }

Write-Host '=== PoMarriedFight secrets ===' -ForegroundColor Cyan
Write-Host "Vault: $Vault   Signed in as: $account" -ForegroundColor Cyan
if (-not $Apply) { Write-Host 'DRY RUN — nothing will be written. Re-run with -Apply to carry this out.' -ForegroundColor Yellow }
Write-Host ''

$existing = @(az keyvault secret list --vault-name $Vault --query '[].name' -o tsv)
if (-not $existing) { throw "Could not read secret names from $Vault. Check the vault name and your access." }

$clientId = $null

foreach ($entry in $Plan) {
    $target = $entry.Target
    $source = $entry.Sources | Where-Object { $existing -contains $_ } | Select-Object -First 1

    if (-not $source) {
        if ($entry.Required) {
            Write-Todo "$target — no source secret found (looked for: $($entry.Sources -join ', ')). Set it by hand."
        } else {
            Write-Skipped "$target — no source secret; that provider is simply not configured."
        }
        continue
    }

    if (($existing -contains $target) -and -not $Force) {
        Write-Skipped "$target already exists — left alone (pass -Force to overwrite)."
        if ($target -eq 'PoMarriedFight--AzureAd--ClientId') {
            $clientId = az keyvault secret show --vault-name $Vault --name $target --query value -o tsv
        }
        continue
    }

    if (-not $Apply) {
        Write-Plan "$source  ->  $target"
        continue
    }

    # Through a file, never a command-line argument: an argument is visible in a process list.
    $temp = New-TemporaryFile
    try {
        az keyvault secret download --vault-name $Vault --name $source --file $temp.FullName --overwrite | Out-Null
        az keyvault secret set --vault-name $Vault --name $target --file $temp.FullName -o none
        Write-Ok "$target  (from $source)"
        if ($target -eq 'PoMarriedFight--AzureAd--ClientId') { $clientId = (Get-Content $temp.FullName -Raw).Trim() }
    } finally {
        # Overwrite before deleting: a secret left in free space is still a secret.
        if (Test-Path $temp.FullName) {
            Set-Content -Path $temp.FullName -Value ('0' * 512) -NoNewline
            Remove-Item $temp.FullName -Force
        }
    }
}

# ── The redirect URI ────────────────────────────────────────────────────────────
# The site signs in with the same Entra registration as the app it was merged from, so that registration has to
# know this site's callback or sign-in fails with AADSTS50011 and nothing else in the app can be reached.
if ($SkipRedirectUri) {
    Write-Host ''
    Write-Skipped 'Redirect URI left untouched (-SkipRedirectUri).'
    return
}

Write-Host ''
if (-not $clientId) {
    if (-not $Apply) {
        Write-Plan "Then add $RedirectUri to the app registration named by PoMarriedFight--AzureAd--ClientId."
    } else {
        Write-Todo 'No client id was resolved, so the redirect URI was not touched.'
    }
    return
}

$app = az ad app list --app-id $clientId --query '[0].{id:id, name:displayName, uris:spa.redirectUris}' -o json 2>$null | ConvertFrom-Json
if (-not $app) {
    Write-Todo "The app registration for that client id could not be read; add $RedirectUri to it by hand."
    return
}

$uris = @($app.uris)
Write-Host "App registration: $($app.name)" -ForegroundColor Cyan
foreach ($uri in $uris) { Write-Host "  already: $uri" -ForegroundColor DarkGray }

if ($uris -contains $RedirectUri) {
    Write-Ok "$RedirectUri is already registered."
    return
}

if (-not $Apply) {
    Write-Plan "add SPA redirect URI: $RedirectUri"
    return
}

# A single-page app: the WASM client redeems the code itself, so the URI belongs to the SPA platform, not Web.
$all = @($uris + $RedirectUri | Where-Object { $_ } | Select-Object -Unique)
$body = @{ spa = @{ redirectUris = $all } } | ConvertTo-Json -Depth 4 -Compress

# Through a file. Passed inline, Windows takes the braces and quotes apart before az sees them, and Graph answers
# "Unable to read JSON request payload" — which this script used to report as a success.
$bodyFile = New-TemporaryFile
try {
    Set-Content -Path $bodyFile.FullName -Value $body -Encoding utf8NoBOM
    az rest --method PATCH `
        --uri "https://graph.microsoft.com/v1.0/applications/$($app.id)" `
        --headers 'Content-Type=application/json' `
        --body "@$($bodyFile.FullName)" | Out-Null

    if ($LASTEXITCODE -ne 0) {
        throw "Graph refused the change (exit $LASTEXITCODE). The redirect URI was NOT added; add $RedirectUri to $($app.name) by hand."
    }
} finally {
    Remove-Item $bodyFile.FullName -Force -ErrorAction SilentlyContinue
}

Write-Ok "added $RedirectUri"
