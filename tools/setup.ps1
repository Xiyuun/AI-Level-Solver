# LevelForge 一键部署（源码版）
#
# 面向"只想跑起来"的人：自动检测/安装 .NET 10 SDK 与 Node.js（都装在当前用户目录，不需要管理员权限），
# 然后构建后端与前端、部署玩法插件、生成启动脚本。
#
# 用法（一般由 setup.cmd 调用）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\setup.ps1
# 可选参数：
#   -CheckOnly   只检查环境并打印结论，不下载、不构建
#   -SkipTests   跳过测试（构建更快）
#   -NoLaunch    结束时不询问"现在启动"
param(
    [switch]$CheckOnly,
    [switch]$SkipTests,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'

# 注意：本文件必须保存为「UTF-8 带 BOM」，否则 Windows PowerShell 5.1 会把中文读成乱码。
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

# 共享环境探测（.NET / Node 的查找规则与 package.ps1 保持一致）
. (Join-Path $PSScriptRoot '_env.ps1')

function Step($n, $total, $text) {
    Write-Host ""
    Write-Host "[$n/$total] $text" -ForegroundColor Cyan
}

function Ok($text)   { Write-Host "      OK  $text" -ForegroundColor Green }
function Warn($text) { Write-Host "      注意 $text" -ForegroundColor Yellow }
function Die($text, $hint) {
    Write-Host ""
    Write-Host "  失败：$text" -ForegroundColor Red
    if ($hint) { Write-Host "  怎么办：$hint" -ForegroundColor Yellow }
    Write-Host ""
    exit 1
}

Write-Host ""
Write-Host "  LevelForge 一键部署" -ForegroundColor White
Write-Host "  目录：$root" -ForegroundColor DarkGray
Write-Host "  说明：所有依赖都装在当前用户目录，不需要管理员权限；首次部署约需 5~10 分钟（含下载）。" -ForegroundColor DarkGray

$total = 5

# ---------------------------------------------------------------- 1) .NET 10 SDK
Step 1 $total "检查 .NET 10 SDK（构建后端需要）"

$dotnetReady = $false
if (Use-DotNet10) {
    Ok "已就绪：dotnet $(Get-DotNetVersionText)"
    $dotnetReady = $true
}
else {
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) {
        Warn "系统里的 dotnet 不是 10.x（当前 $(Get-DotNetVersionText)），将安装一份到用户目录，不影响系统那份。"
    } else {
        Warn "没有检测到 .NET SDK。"
    }

    if ($CheckOnly) {
        Write-Host "      （-CheckOnly：跳过安装）" -ForegroundColor DarkGray
        exit 2
    }

    Write-Host "      正在下载 .NET SDK 10 安装脚本..." -ForegroundColor Gray
    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    try {
        Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
    } catch {
        Die "下载安装脚本失败（$($_.Exception.Message)）" "检查网络/代理后重试；或手动安装 .NET 10 SDK 后重新运行 setup.cmd"
    }

    $userDotnet = Join-Path $env:USERPROFILE '.dotnet'
    Write-Host "      正在安装 .NET SDK 10 到 $userDotnet（约 300MB，请耐心等待）..." -ForegroundColor Gray
    & powershell -NoProfile -ExecutionPolicy Bypass -File $installer -Channel 10.0 -InstallDir $userDotnet -NoPath
    if ($LASTEXITCODE -ne 0) {
        Die ".NET SDK 安装失败（退出码 $LASTEXITCODE）" "可手动安装 https://dot.net/download 后重新运行 setup.cmd"
    }
    if (-not (Use-DotNet10)) {
        Die "安装完成但没找到可用的 10.x SDK" "打开新窗口执行 dotnet --list-sdks 确认；必要时把 %USERPROFILE%\.dotnet 加入 PATH"
    }
    Ok "安装完成：dotnet $(Get-DotNetVersionText)"
    $dotnetReady = $true
}

# ---------------------------------------------------------------- 2) Node.js
Step 2 $total "检查 Node.js（构建界面需要）"

$nodeReady = Use-Node
if ($nodeReady) { Ok "已就绪：node $(node -v) / npm $(npm -v)" }
if (-not $nodeReady) {
    Warn "没有检测到 Node.js。"
    $portable = Join-Path $env:LOCALAPPDATA 'LevelForge\node'
    if ($CheckOnly) {
        Write-Host "      （-CheckOnly：跳过安装）" -ForegroundColor DarkGray
        exit 2
    }
    Write-Host "      正在下载便携版 Node.js 到 $portable（约 30MB）..." -ForegroundColor Gray
    try {
        $index = Invoke-RestMethod -Uri 'https://nodejs.org/dist/index.json' -UseBasicParsing
        # 优先最新 LTS（当前为 v24.x），否则退到索引里最新的一个
        $lts = $index | Where-Object { $_.lts } | Select-Object -First 1
        if (-not $lts) { $lts = $index | Select-Object -First 1 }
        if (-not $lts) { throw "nodejs.org 索引为空" }
        $ver = $lts.version
        $zip = Join-Path $env:TEMP "node-$ver-win-x64.zip"
        Invoke-WebRequest -Uri "https://nodejs.org/dist/$ver/node-$ver-win-x64.zip" -OutFile $zip -UseBasicParsing
        $stage = Join-Path $env:TEMP "levelforge-node-$ver"
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
        Expand-Archive -Path $zip -DestinationPath $stage -Force
        New-Item -ItemType Directory -Force -Path (Split-Path $portable) | Out-Null
        if (Test-Path $portable) { Remove-Item $portable -Recurse -Force }
        Move-Item (Join-Path $stage "node-$ver-win-x64") $portable
        Remove-Item $stage -Recurse -Force
        $env:PATH = "$portable;$env:PATH"
        $nodeReady = Test-Node
    } catch {
        Warn "自动安装 Node.js 失败：$($_.Exception.Message)"
    }
    if ($nodeReady) { Ok "安装完成：node $(node -v)" }
    else {
        Warn "没有 Node.js，界面（web/dist）将无法构建：平台只能以 API 方式使用。"
        Warn "如需界面：装好 Node.js 后重新运行 setup.cmd（或直接使用发行包 publish\，里面已带界面）。"
    }
}

if ($CheckOnly) {
    Write-Host ""
    Write-Host "  环境检查完成：可以运行 setup.cmd 开始部署。" -ForegroundColor Green
    Write-Host ""
    exit 0
}

# ---------------------------------------------------------------- 3) 构建后端 + 前端
Step 3 $total "构建后端" + $(if ($SkipTests) { "（跳过测试）" } else { " + 全量测试" })

# 正在运行的平台会锁住 Debug 产物（MSB3021），构建前必须先关掉
$running = Get-Process -Name 'LevelForge.Platform' -ErrorAction SilentlyContinue
if ($running) {
    Warn "检测到 LevelForge 正在运行（会锁住构建产物，导致构建失败）。"
    $answer = Read-Host "      现在关闭它继续部署吗？(Y/n)"
    if ($answer -eq '' -or $answer -match '^[Yy]') {
        $running | Stop-Process -Force
        Start-Sleep -Seconds 2
        Ok "已关闭正在运行的实例"
    } else {
        Die "构建需要先关闭 LevelForge" "关掉它的黑色窗口后重新运行 setup.cmd"
    }
}

$buildArgs = @('build', 'LevelForge.slnx', '-c', 'Debug', '--nologo')
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { Die "后端构建失败" "把上面的报错发出来；常见原因是 .NET SDK 版本不对或网络无法还原 NuGet 包（仓库已配华为云镜像）" }
Ok "后端构建完成"

if (-not $SkipTests) {
    Write-Host "      正在跑全量测试（约 1 分钟）..." -ForegroundColor Gray
    & dotnet test LevelForge.slnx -c Debug --no-build --nologo
    if ($LASTEXITCODE -ne 0) { Die "测试未通过" "先解决测试失败再部署；只想先跑起来可以加 -SkipTests" }
    Ok "测试全绿"
}

if ($nodeReady) {
    Write-Host "      正在构建界面..." -ForegroundColor Gray
    Push-Location (Join-Path $root 'web')
    try {
        if (-not (Test-Path 'node_modules')) {
            Write-Host "      首次构建：安装前端依赖（约 1~3 分钟）..." -ForegroundColor Gray
            & npm install --no-audit --no-fund
            if ($LASTEXITCODE -ne 0) { throw "npm install 失败（退出码 $LASTEXITCODE）" }
        }
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw "npm run build 失败（退出码 $LASTEXITCODE）" }
    } catch {
        Pop-Location
        Die "界面构建失败：$_" "检查网络后重试；或先跑 build.cmd 看详细报错"
    }
    Pop-Location
    Ok "界面构建完成（web/dist）"
}

# ---------------------------------------------------------------- 4) 玩法插件
Step 4 $total "部署玩法插件（PiggyFarm 等）"

Push-Location $root
try {
    & cmd /c "deploy-plugins.cmd"
    if ($LASTEXITCODE -ne 0) { throw "插件部署失败" }
} catch {
    Pop-Location
    Die "$_" "确认插件工程能单独构建：dotnet build adapters\piggyfarm\LevelForge.Adapters.PiggyFarm"
}
Pop-Location
Ok "插件已放入 src\LevelForge.Platform\data\plugins\"

# ---------------------------------------------------------------- 5) 启动脚本 + 自检
Step 5 $total "生成启动脚本并自检"

$startCmd = Join-Path $root 'start.cmd'
if (-not (Test-Path $startCmd)) {
    Warn "仓库里没有 start.cmd（应当随仓库提供）"
} else {
    Ok "启动脚本就绪：start.cmd"
}

$dist = Join-Path $root 'web\dist\index.html'
if (Test-Path $dist) { Ok "界面产物就绪：web\dist" } else { Warn "缺少 web\dist（界面不可用，仅 API）" }

$plugin = Join-Path $root 'src\LevelForge.Platform\data\plugins\piggyfarm\LevelForge.Adapters.PiggyFarm.dll'
if (Test-Path $plugin) { Ok "PiggyFarm 插件就绪" } else { Warn "PiggyFarm 插件缺失（生成/求解该玩法会不可用）" }

Write-Host ""
Write-Host "  部署完成。" -ForegroundColor Green
Write-Host "  1) 双击 start.cmd 启动（浏览器会自动打开 http://localhost:5000）" -ForegroundColor White
Write-Host "  2) 首次使用：设置页填写 LLM 地址 / Key / 模型（出题功能需要；导入/求解/质检不需要）" -ForegroundColor White
Write-Host "  3) 想分发给别人：运行 package.cmd，产物在 publish\（对方解压即用，不需要装任何东西）" -ForegroundColor White
Write-Host ""

if (-not $NoLaunch -and -not $CheckOnly) {
    $answer = Read-Host "  现在启动 LevelForge 吗？(Y/n)"
    if ($answer -eq '' -or $answer -match '^[Yy]') {
        Start-Process -FilePath (Join-Path $root 'start.cmd') -WorkingDirectory $root
    }
}
