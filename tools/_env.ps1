# 共享环境探测（由 setup.ps1 / package.ps1 点引用）
# 约定与仓库既有脚本一致：优先用 %USERPROFILE%\.dotnet 的 10.x SDK、%USERPROFILE%\nodejs 的便携 Node。

function Test-DotNet10 {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $cmd) { return $false }
    try { $sdks = & dotnet --list-sdks 2>$null } catch { return $false }
    foreach ($line in $sdks) {
        if ($line -match '^10\.') { return $true }
    }
    return $false
}

# 让 `dotnet` 指向一个有 10.x SDK 的安装（优先用户目录，避免系统里旧 SDK 抢先）
function Use-DotNet10 {
    if (Test-DotNet10) { return $true }
    $userDotnet = Join-Path $env:USERPROFILE '.dotnet'
    if (Test-Path (Join-Path $userDotnet 'dotnet.exe')) {
        $env:PATH = "$userDotnet;$env:PATH"
        $env:DOTNET_ROOT = $userDotnet
        if (Test-DotNet10) { return $true }
    }
    return $false
}

function Test-Node {
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) { return $false }
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { return $false }
    return $true
}

# 让 `node`/`npm` 可用（PATH → %USERPROFILE%\nodejs → %LOCALAPPDATA%\LevelForge\node）
function Use-Node {
    if (Test-Node) { return $true }
    foreach ($dir in @((Join-Path $env:USERPROFILE 'nodejs'), (Join-Path $env:LOCALAPPDATA 'LevelForge\node'))) {
        if (Test-Path (Join-Path $dir 'node.exe')) {
            $env:PATH = "$dir;$env:PATH"
            if (Test-Node) { return $true }
        }
    }
    return $false
}

function Get-DotNetVersionText {
    try { return (& dotnet --version 2>$null) } catch { return 'unknown' }
}
