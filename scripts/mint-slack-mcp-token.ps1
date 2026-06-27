# Mints a Slack USER OAuth token (xoxp-...) for use as the static bearer token on Slack's hosted MCP
# server (https://mcp.slack.com/mcp). Slack's MCP server requires a USER token (not a bot token) and
# does NOT support the automatic "Dynamic Client Registration" OAuth flow — so this script performs the
# manual authorization-code flow once: it opens Slack's consent screen, captures the returned code on a
# local loopback listener, and exchanges it for the user token. Paste the printed token into the
# Messaging connector's "MCP Auth Token" field, then click Check Health.
#
# Usage:
#   ./scripts/mint-slack-mcp-token.ps1 -ClientId <client-id>          # secret via $env:SLACK_CLIENT_SECRET
#   ./scripts/mint-slack-mcp-token.ps1 -ClientId <client-id> -ClientSecret <secret>
#
# One-time Slack app setup (https://api.slack.com/apps -> your app):
#   - OAuth & Permissions -> User Token Scopes: include the scopes requested below (at minimum chat:write).
#   - OAuth & Permissions -> Redirect URLs: add exactly the RedirectUri this script uses (default
#     http://localhost:8080/callback), then Save.
#   - Enable the app for Slack MCP server access (the app must be an internal or directory-published app).

[CmdletBinding()]
param(
    # The Slack app's Client ID (shown on the app's Basic Information page; not a secret).
    [Parameter(Mandatory = $true)]
    [string] $ClientId,

    # The Slack app's Client Secret. Prefer the SLACK_CLIENT_SECRET environment variable so the secret
    # never lands in shell history or this command line.
    [string] $ClientSecret = $env:SLACK_CLIENT_SECRET,

    # User-token scopes to request. chat:write is the minimum needed to send a message; the others let
    # the MCP tools read channels and users. Override with -Scopes if you need a different set.
    [string[]] $Scopes = @('chat:write', 'channels:read', 'channels:history', 'users:read'),

    # Loopback port for the OAuth redirect. MUST match the Redirect URL registered in the Slack app.
    [int] $Port = 8080
)

$ErrorActionPreference = 'Stop'

# ── Guard clauses ─────────────────────────────────────────────────────────────────
if ([string]::IsNullOrWhiteSpace($ClientSecret)) {
    throw "Client secret not provided. Pass -ClientSecret, or set `$env:SLACK_CLIENT_SECRET (find it on the app's Basic Information page, behind 'Show')."
}

$redirectUri      = "http://localhost:$Port/callback"
$requestedScopes  = ($Scopes -join ',')

# user_scope (NOT scope) requests USER-token scopes — exactly what Slack's MCP server requires.
$authorizeUrl = 'https://slack.com/oauth/v2/authorize?' +
    "client_id=$([uri]::EscapeDataString($ClientId))" +
    "&user_scope=$([uri]::EscapeDataString($requestedScopes))" +
    "&redirect_uri=$([uri]::EscapeDataString($redirectUri))"

# ── 1. Start a loopback listener for Slack's OAuth redirect ─────────────────────────
$listener = [System.Net.HttpListener]::new()
$listener.Prefixes.Add("http://localhost:$Port/")
try {
    $listener.Start()
}
catch {
    throw "Could not listen on http://localhost:$Port/. Is the port already in use? ($($_.Exception.Message))"
}

Write-Host 'Opening the Slack consent screen in your browser...' -ForegroundColor Cyan
Write-Host "If it does not open automatically, paste this URL into a browser:`n  $authorizeUrl`n"
Start-Process $authorizeUrl

Write-Host "Waiting for Slack to redirect to $redirectUri (approve access in the browser)..." -ForegroundColor Cyan

# ── 2. Capture the authorization code from the redirect ─────────────────────────────
$authorizationCode = $null
$authorizationError = $null
try {
    while ($null -eq $authorizationCode -and $null -eq $authorizationError) {
        $context  = $listener.GetContext()   # blocks until the browser hits the loopback redirect
        $request  = $context.Request
        $response = $context.Response

        $authorizationCode  = $request.QueryString['code']
        $authorizationError = $request.QueryString['error']

        $browserMessage = if ($authorizationCode) {
            'Slack authorization complete. You can close this tab and return to the terminal.'
        }
        else {
            "Slack authorization failed: $authorizationError. You can close this tab."
        }
        $responseBytes = [System.Text.Encoding]::UTF8.GetBytes(
            "<html><body style='font-family:sans-serif;padding:2rem'>$browserMessage</body></html>")
        $response.ContentType = 'text/html'
        $response.OutputStream.Write($responseBytes, 0, $responseBytes.Length)
        $response.OutputStream.Close()
    }
}
finally {
    $listener.Stop()
}

if ($authorizationError) {
    throw "Slack returned an authorization error: $authorizationError"
}
Write-Host 'Authorization code received. Exchanging it for a user token...' -ForegroundColor Cyan

# ── 3. Exchange the code for the user access token ──────────────────────────────────
# Slack returns the bot token at the top level and the USER token under authed_user — we want the latter.
$tokenResponse = Invoke-RestMethod -Method Post -Uri 'https://slack.com/api/oauth.v2.access' -Body @{
    client_id     = $ClientId
    client_secret = $ClientSecret
    code          = $authorizationCode
    redirect_uri  = $redirectUri
}

if (-not $tokenResponse.ok) {
    throw "Slack token exchange failed: $($tokenResponse.error)"
}

$userToken = $tokenResponse.authed_user.access_token
if ([string]::IsNullOrWhiteSpace($userToken)) {
    throw 'Token exchange succeeded but no user token was returned. Confirm you requested USER scopes (user_scope), not bot scopes.'
}

# ── 4. Hand the token to the operator ───────────────────────────────────────────────
Write-Host ''
Write-Host '────────────────────────────────────────────────────────────' -ForegroundColor Green
Write-Host 'Slack USER token (xoxp-) minted. Paste it into the Messaging' -ForegroundColor Green
Write-Host 'connector''s "MCP Auth Token" field, then click Check Health:' -ForegroundColor Green
Write-Host ''
Write-Host "  $userToken"
Write-Host ''
Write-Host "Granted user scopes: $($tokenResponse.authed_user.scope)" -ForegroundColor DarkGray
Write-Host 'Treat this token like a password — it acts on your Slack identity.' -ForegroundColor DarkGray
Write-Host '────────────────────────────────────────────────────────────' -ForegroundColor Green
