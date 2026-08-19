# 模擬長對話負載，量測各 --ctx-size 在真實使用下的記憶體與速度曲線。
#
# 與 bench-ctx-vram.ps1 的差別：
#   前者只量「剛啟動、KV cache 還是空的」瞬間，會嚴重低估實際用量；
#   本腳本會實際把上下文填到指定深度，觀察 KV cache 長大後的狀況。
#
# 用法：powershell -ExecutionPolicy Bypass -File tools\bench-ctx-load.ps1
$ErrorActionPreference = 'Continue'

$server = 'C:\ai-lab\llama-vulkan-b10488\llama-server.exe'
$model  = 'C:\ai-lab\models\qwen\Qwen3.8-27B-Official\Qwen3.8-27B-Q4_K_M.gguf'
$mmproj = 'C:\ai-lab\models\qwen\Qwen3.8-27B-Official\mmproj-F16.gguf'
$tmpl   = 'C:\ai-lab\models\qwen\Qwen3.8-27B-Official\chat_template.jinja'
$outDir = 'C:\ai-lab\llama-vulkan-app\tools\bench-out'
$log    = Join-Path $outDir 'ctx-load.log'
$csv    = Join-Path $outDir 'ctx-load.csv'

# 要測的 ctx，以及每組要推進到的上下文深度（token）
$ctxList = @(16384, 32768, 65536)
$depths  = @(2000, 8000, 16000, 24000, 30000)

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
"=== 長對話負載測試 $(Get-Date -Format 'HH:mm:ss') ===" | Out-File $log -Encoding utf8
'ctx,depth,tps,privGB,wsGB,vramLocalGB,vramSpillGB,freeRamGB,pagesIn' |
    Out-File $csv -Encoding utf8

# ---------- 量測工具 ----------

function Get-Metrics([int]$procId) {
    $priv = 0.0; $ws = 0.0
    try {
        $p = Get-Process -Id $procId -ErrorAction Stop
        $priv = [math]::Round($p.PrivateMemorySize64 / 1GB, 2)
        $ws   = [math]::Round($p.WorkingSet64 / 1GB, 2)
    } catch { }

    # 顯示卡記憶體：local = 真的在顯卡上，non local = 已外溢到系統記憶體。
    # 外溢一旦出現，推論就得走 PCIe，速度會明顯下滑。
    $local = 0.0; $spill = 0.0
    $samples = (Get-Counter "\GPU Process Memory(pid_${procId}*)\*" -ErrorAction SilentlyContinue).CounterSamples
    foreach ($s in $samples) {
        if ($s.Path -like '*local usage*' -and $s.Path -notlike '*non local*') {
            if ($s.CookedValue / 1GB -gt $local) { $local = [math]::Round($s.CookedValue / 1GB, 2) }
        }
        if ($s.Path -like '*non local usage*') {
            if ($s.CookedValue / 1GB -gt $spill) { $spill = [math]::Round($s.CookedValue / 1GB, 2) }
        }
    }

    $free = [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 2)
    $pin = 0.0
    $c = (Get-Counter '\Memory\Pages Input/sec' -ErrorAction SilentlyContinue).CounterSamples
    if ($c) { $pin = [math]::Round($c[0].CookedValue, 0) }

    [PSCustomObject]@{
        Priv = $priv; Ws = $ws; VramLocal = $local
        VramSpill = $spill; Free = $free; PagesIn = $pin
    }
}

function Wait-Server([int]$timeoutSec) {
    for ($i = 0; $i -lt $timeoutSec; $i++) {
        Start-Sleep -Seconds 1
        try {
            $null = Invoke-RestMethod 'http://127.0.0.1:8080/health' -TimeoutSec 2
            return $true
        } catch { }
    }
    return $false
}

# 送出指定長度的 prompt 並生成，回傳實測 tokens/sec。
function Invoke-Probe([string]$prompt, [int]$nPredict) {
    $body = @{
        prompt = $prompt
        n_predict = $nPredict
        temperature = 0.7
        cache_prompt = $true
    } | ConvertTo-Json -Compress

    try {
        $r = Invoke-RestMethod 'http://127.0.0.1:8080/completion' `
            -Method Post -Body $body -ContentType 'application/json' -TimeoutSec 600
        if ($r.timings -and $r.timings.predicted_per_second) {
            return [math]::Round($r.timings.predicted_per_second, 2)
        }
    } catch {
        Write-Output ('    probe failed: ' + $_.Exception.Message)
    }
    return 0
}

# ---------- 主測試 ----------

# 用來墊出指定長度上下文的填充文字（內容不重要，重點是 token 數）。
$filler = 'The quick brown fox jumps over the lazy dog near the riverbank at dawn. '

foreach ($ctx in $ctxList) {
    "--- ctx = $ctx  $(Get-Date -Format 'HH:mm:ss') ---" | Out-File $log -Append -Encoding utf8

    $sArgs = @(
        '--model', $model, '--mmproj', $mmproj, '--chat-template-file', $tmpl,
        '--device', 'Vulkan1', '--ctx-size', "$ctx", '--n-gpu-layers', '99',
        '--spec-type', 'draft-mtp', '--spec-draft-n-max', '2', '--spec-draft-p-min', '0.1',
        '--spec-draft-type-k', 'q8_0', '--spec-draft-type-v', 'q8_0',
        '-ub', '256', '-fa', 'on', '--reasoning', 'off', '--reasoning-preserve',
        '--cache-ram', '4096', '--parallel', '1',
        '--cache-type-k', 'q8_0', '--cache-type-v', 'q8_0',
        '--host', '127.0.0.1', '--port', '8080', '--threads', '3', '--threads-batch', '3'
    )
    $p = Start-Process -FilePath $server -ArgumentList $sArgs `
        -RedirectStandardOutput (Join-Path $outDir "load-$ctx.out") `
        -RedirectStandardError  (Join-Path $outDir "load-$ctx.err") `
        -NoNewWindow -PassThru

    if (-not (Wait-Server 240)) {
        "  啟動失敗或逾時，跳過。" | Out-File $log -Append -Encoding utf8
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
        Start-Sleep -Seconds 15
        continue
    }

    $m = Get-Metrics $p.Id
    ("  [啟動完成] priv={0} ws={1} vram={2} 外溢={3} 可用RAM={4}" -f
        $m.Priv, $m.Ws, $m.VramLocal, $m.VramSpill, $m.Free) |
        Out-File $log -Append -Encoding utf8

    foreach ($d in $depths) {
        if ($d -ge $ctx - 2000) { continue }   # 留空間給生成，避免超出上限

        # 依目標深度組出夠長的 prompt（filler 約 15 token）
        $repeat = [int]($d / 15)
        $sb = New-Object System.Text.StringBuilder
        for ($i = 0; $i -lt $repeat; $i++) { [void]$sb.Append($filler) }
        [void]$sb.Append("`n請用一句話總結上面的內容。")

        $tps = Invoke-Probe $sb.ToString() 128
        Start-Sleep -Seconds 3
        $m = Get-Metrics $p.Id

        ("  深度 {0,6} → {1,6} t/s | priv={2} ws={3} vram={4} 外溢={5} 可用RAM={6} pagesIn={7}" -f
            $d, $tps, $m.Priv, $m.Ws, $m.VramLocal, $m.VramSpill, $m.Free, $m.PagesIn) |
            Out-File $log -Append -Encoding utf8

        ('{0},{1},{2},{3},{4},{5},{6},{7},{8}' -f
            $ctx, $d, $tps, $m.Priv, $m.Ws, $m.VramLocal, $m.VramSpill, $m.Free, $m.PagesIn) |
            Out-File $csv -Append -Encoding utf8
    }

    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 15
}

"=== 完成 $(Get-Date -Format 'HH:mm:ss') ===" | Out-File $log -Append -Encoding utf8
