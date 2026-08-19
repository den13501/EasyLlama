# 量測不同 --ctx-size 與 --cache-ram 的 VRAM / RAM 實際佔用。
# 用法：powershell -ExecutionPolicy Bypass -File tools\bench-ctx-vram.ps1
$ErrorActionPreference = 'Continue'

$server = 'C:\ai-lab\llama-vulkan-b10488\llama-server.exe'
$model = 'C:\ai-lab\models\qwen\Qwen3.8-27B-Official\Qwen3.8-27B-Q4_K_M.gguf'
$mmproj = 'C:\ai-lab\models\qwen\Qwen3.8-27B-Official\mmproj-F16.gguf'
$outDir = 'C:\ai-lab\llama-vulkan-app\tools\bench-out'
$log = Join-Path $outDir 'ctx-vram.log'

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
"=== ctx / cache-ram 佔用量測 $(Get-Date -Format 'HH:mm:ss') ===" |
    Out-File $log -Encoding utf8

# 測試組合：ctx 大小 + cache-ram
$cases = @(
    @{ Ctx = 8192;   Cram = 4096 },
    @{ Ctx = 32768;  Cram = 4096 },
    @{ Ctx = 65536;  Cram = 4096 },
    @{ Ctx = 131072; Cram = 4096 },
    @{ Ctx = 32768;  Cram = 16384 }
)

function Get-VramGB {
    $s = (Get-Counter '\GPU Process Memory(*)\Total Committed' -ErrorAction SilentlyContinue).CounterSamples |
        Where-Object { $_.InstanceName -like '*f142*' -and $_.CookedValue -gt 1GB }
    if ($s) { [math]::Round((($s | Measure-Object CookedValue -Maximum).Maximum) / 1GB, 2) } else { 0 }
}

foreach ($c in $cases) {
    $ctx = $c.Ctx
    $cram = $c.Cram
    "--- ctx=$ctx cache-ram=$cram ---" | Out-File $log -Append -Encoding utf8

    $sArgs = @(
        '--model', $model, '--mmproj', $mmproj,
        '--device', 'Vulkan1', '--ctx-size', "$ctx", '--n-gpu-layers', '99',
        '-ub', '256', '-fa', 'on', '--cache-type-k', 'q8_0', '--cache-type-v', 'q8_0',
        '--cache-ram', "$cram", '--parallel', '1',
        '--host', '127.0.0.1', '--port', '8080', '--threads', '3', '--no-warmup'
    )
    $p = Start-Process -FilePath $server -ArgumentList $sArgs `
        -RedirectStandardOutput (Join-Path $outDir "srv-$ctx-$cram.out") `
        -RedirectStandardError  (Join-Path $outDir "srv-$ctx-$cram.err") `
        -NoNewWindow -PassThru

    # 等待伺服器就緒（最多 180 秒）
    $ready = $false
    for ($i = 0; $i -lt 180; $i++) {
        Start-Sleep -Seconds 1
        if ($p.HasExited) { break }
        try {
            $null = Invoke-RestMethod 'http://127.0.0.1:8080/health' -TimeoutSec 2
            $ready = $true
            break
        } catch { }
    }

    if (-not $ready) {
        $tail = Get-Content (Join-Path $outDir "srv-$ctx-$cram.err") -Tail 3 -ErrorAction SilentlyContinue
        "  啟動失敗（可能顯存不足）： $($tail -join ' / ')" | Out-File $log -Append -Encoding utf8
        if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
        Start-Sleep -Seconds 10
        continue
    }

    Start-Sleep -Seconds 5
    $p.Refresh()
    $ram = [math]::Round($p.PrivateMemorySize64 / 1GB, 2)
    $vram = Get-VramGB
    $free = [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 2)

    ("  VRAM={0} GB  行程RAM={1} GB  系統可用RAM={2} GB" -f $vram, $ram, $free) |
        Out-File $log -Append -Encoding utf8

    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 12
}

"=== 完成 $(Get-Date -Format 'HH:mm:ss') ===" | Out-File $log -Append -Encoding utf8
