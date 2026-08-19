# 比較 --load-mode none / mmap 對速度與記憶體的影響。
# 用法：powershell -ExecutionPolicy Bypass -File tools\bench-loadmode.ps1
$ErrorActionPreference = 'Stop'

$bench = 'C:\ai-lab\llama-vulkan-b10488\llama-bench.exe'
$model = 'C:\ai-lab\models\qwen\Qwen3.8-27B-Official\Qwen3.8-27B-Q4_K_M.gguf'
$outDir = 'C:\ai-lab\llama-vulkan-app\tools\bench-out'

if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

function Get-MemSnapshot {
    $os = Get-CimInstance Win32_OperatingSystem
    $pf = Get-CimInstance Win32_PageFileUsage
    [PSCustomObject]@{
        FreeRAM_GB   = [math]::Round($os.FreePhysicalMemory / 1MB, 2)
        PageUsed_GB  = [math]::Round(($pf | Measure-Object CurrentUsage -Sum).Sum / 1KB, 2)
        PagePeak_GB  = [math]::Round(($pf | Measure-Object PeakUsage -Sum).Sum / 1KB, 2)
    }
}

$log = Join-Path $outDir 'run.log'
"=== 基準測試開始 $(Get-Date -Format 'HH:mm:ss') ===" | Out-File $log -Encoding utf8
("測試前記憶體： " + (Get-MemSnapshot | Out-String).Trim()) | Out-File $log -Append -Encoding utf8

foreach ($mode in @('mmap', 'none')) {
    ("--- load-mode = $mode  開始 $(Get-Date -Format 'HH:mm:ss') ---") |
        Out-File $log -Append -Encoding utf8

    $json = Join-Path $outDir "loadmode-$mode.json"
    $err = Join-Path $outDir "loadmode-$mode.err"

    # 前景等待跑完，記憶體峰值另外由監測迴圈記錄。
    # 注意：不可命名為 $args，那是 PowerShell 保留變數。
    $benchArgs = @(
        '-m', $model, '-dev', 'Vulkan1', '-ngl', '99', '-fa', 'on',
        '-ctk', 'q8_0', '-ctv', 'q8_0', '-ub', '256', '-t', '3',
        '-lm', $mode, '-p', '512', '-n', '128', '-d', '0,8192',
        '-r', '3', '-o', 'json'
    )
    $proc = Start-Process -FilePath $bench -ArgumentList $benchArgs `
        -RedirectStandardOutput $json -RedirectStandardError $err `
        -NoNewWindow -PassThru

    # 每秒取樣，記錄這一輪的記憶體峰值
    $peakWs = 0.0
    $minFree = 999.0
    while (-not $proc.HasExited) {
        Start-Sleep -Seconds 1
        try {
            $proc.Refresh()
            $ws = $proc.WorkingSet64 / 1GB
            if ($ws -gt $peakWs) { $peakWs = $ws }
        } catch { }
        $free = (Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB
        if ($free -lt $minFree) { $minFree = $free }
    }

    ("  峰值工作集： {0} GB / 最低可用記憶體： {1} GB" -f
        [math]::Round($peakWs, 2), [math]::Round($minFree, 2)) |
        Out-File $log -Append -Encoding utf8
    ("  結束後： " + (Get-MemSnapshot | Out-String).Trim()) |
        Out-File $log -Append -Encoding utf8

    # 等待 OS 回收頁面，避免影響下一組
    Start-Sleep -Seconds 20
}

"=== 結果彙整 ===" | Out-File $log -Append -Encoding utf8
("{0,-6} {1,-14} {2,10} {3,8}" -f '模式', '項目', '速度(t/s)', '誤差') |
    Out-File $log -Append -Encoding utf8

foreach ($mode in @('mmap', 'none')) {
    $json = Join-Path $outDir "loadmode-$mode.json"
    if (-not (Test-Path $json)) { continue }
    $raw = Get-Content $json -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) { continue }
    $rows = $raw | ConvertFrom-Json
    foreach ($r in $rows) {
        $label = if ($r.n_prompt -gt 0) { "pp$($r.n_prompt)" } else { "tg$($r.n_gen)" }
        if ($r.n_depth -gt 0) { $label += "@d$($r.n_depth)" }
        ("{0,-6} {1,-14} {2,10} {3,8}" -f $mode, $label,
            [math]::Round($r.avg_ts, 2), [math]::Round($r.stddev_ts, 2)) |
            Out-File $log -Append -Encoding utf8
    }
}

"=== 測試完成 $(Get-Date -Format 'HH:mm:ss') ===" | Out-File $log -Append -Encoding utf8
