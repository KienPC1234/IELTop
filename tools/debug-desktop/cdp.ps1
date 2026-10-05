# Drives the running desktop app over the WebView2 debug port (CDP), so a flow
# can be checked without guessing and without opening a window by hand.
#
# The app must already be running with the debug port open (tools/debug-desktop/launch.cmd).
# This script:
#   - waits for the page and the Blazor bridge,
#   - runs the JavaScript you pass and prints the value,
#   - can save a screenshot with Page.captureScreenshot (no window needed on screen).
#
# Examples:
#   pwsh -File cdp.ps1 -Js "document.title"
#   pwsh -File cdp.ps1 -Js "[...document.querySelectorAll('[role=tab]')].length"
#   pwsh -File cdp.ps1 -Click "Diagnostics"
#   pwsh -File cdp.ps1 -Click "Practice" -Js "[...document.querySelectorAll('button')].map(b=>b.textContent.trim()).filter(Boolean).slice(0,8)"
#   pwsh -File cdp.ps1 -Shot "C:\Users\me\AppData\Local\Temp\shot.png"
#
# The page URL carries an access key (?k=...). This script never prints it.

param(
    [int]$Port = 22600,
    [string]$Js = "",
    [string]$Click = "",
    [string]$Shot = "",
    [int]$WaitSeconds = 45
)

$ErrorActionPreference = "Continue"
$deadline = (Get-Date).AddSeconds($WaitSeconds)

# Find the page websocket. Prefer the main Blazor page (0.0.0.1), else any page.
$target = $null
while (-not $target -and (Get-Date) -lt $deadline) {
    try {
        $list = @(Invoke-RestMethod "http://127.0.0.1:$Port/json/list" -TimeoutSec 2)
        $target = $list | Where-Object { "$($_.url)" -like "*0.0.0.1*" } | Select-Object -First 1
        if (-not $target) { $target = $list | Select-Object -First 1 }
    } catch { }
    if (-not $target) { Start-Sleep -Milliseconds 800 }
}
if (-not $target) { Write-Output "No debug target on port $Port. Is the app running with launch.cmd?"; exit 1 }

$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$ws.ConnectAsync([Uri]$target.webSocketDebuggerUrl, [System.Threading.CancellationToken]::None).Wait()
$buf = New-Object byte[] 33554432
$global:mid = 0

function Receive([int]$ms) {
    if ($null -eq $ws -or $ws.State -ne 'Open') { return $null }
    $t = $ws.ReceiveAsync([ArraySegment[byte]]::new($buf), [System.Threading.CancellationToken]::None)
    if (-not $t.Wait($ms)) { return $null }
    return [Text.Encoding]::UTF8.GetString($buf, 0, $t.Result.Count)
}

function Evaluate([string]$expr, [int]$seconds = 20) {
    $global:mid++; $id = $global:mid
    $payload = @{ id = $id; method = "Runtime.evaluate"; params = @{ expression = $expr; returnByValue = $true; awaitPromise = $true } } |
        ConvertTo-Json -Depth 8 -Compress
    $ws.SendAsync([ArraySegment[byte]]::new([Text.Encoding]::UTF8.GetBytes($payload)),
        [System.Net.WebSockets.WebSocketMessageType]::Text, $true, [System.Threading.CancellationToken]::None).Wait()
    $end = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $end) {
        $txt = Receive 300
        if (-not $txt) { continue }
        try { $o = $txt | ConvertFrom-Json } catch { continue }
        if ($o.id -eq $id -and $o.result) {
            if ($o.result.exceptionDetails) { return "EXC: " + $o.result.exceptionDetails.exception.description }
            return $o.result.result.value
        }
    }
    return "(timeout)"
}

function Screenshot([string]$file) {
    $global:mid++; $id = $global:mid
    $payload = @{ id = $id; method = "Page.captureScreenshot"; params = @{ format = "png" } } | ConvertTo-Json -Compress
    $ws.SendAsync([ArraySegment[byte]]::new([Text.Encoding]::UTF8.GetBytes($payload)),
        [System.Net.WebSockets.WebSocketMessageType]::Text, $true, [System.Threading.CancellationToken]::None).Wait()
    $end = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $end) {
        $txt = Receive 300
        if (-not $txt) { continue }
        try { $o = $txt | ConvertFrom-Json } catch { continue }
        if ($o.id -eq $id -and $o.result) {
            [IO.File]::WriteAllBytes($file, [Convert]::FromBase64String($o.result.data))
            return "saved $file"
        }
    }
    return "(timeout)"
}

# Wait for the Blazor bridge to expose itself, so a call does not race startup.
for ($i = 0; $i -lt 40; $i++) {
    if ([bool](Evaluate '!!window.__ieltopsBridge')) { break }
    Start-Sleep -Milliseconds 500
}

if ($Click) {
    # Radix tabs and most buttons react on pointerdown, not a plain click.
    $clickJs = @"
(function(){
  var t=[...document.querySelectorAll('[role=tab],button,nav button')].find(function(x){return x.textContent.trim().indexOf('$Click')>=0});
  if(!t) return 'not found: $Click';
  var r=t.getBoundingClientRect();
  var o={bubbles:true,cancelable:true,clientX:r.left+r.width/2,clientY:r.top+r.height/2,button:0};
  t.dispatchEvent(new PointerEvent('pointerdown',o));
  t.dispatchEvent(new MouseEvent('mousedown',o));
  t.focus();
  t.dispatchEvent(new PointerEvent('pointerup',o));
  t.dispatchEvent(new MouseEvent('mouseup',o));
  t.dispatchEvent(new MouseEvent('click',o));
  return 'clicked: $Click';
})()
"@
    Write-Output (Evaluate $clickJs)
    Start-Sleep -Seconds 2
}

if ($Js) { Write-Output (Evaluate $Js) }
if ($Shot) { Write-Output (Screenshot $Shot) }

$ws.Dispose()
