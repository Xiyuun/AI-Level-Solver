# LevelForge 一键打包（发行版）
#
# 产物：publish\  ——  自包含 Windows x64 目录（内置 .NET 运行时 + 界面 + 玩法插件）
#       双击 publish\start.cmd 即可运行，目标机器不需要装 .NET、不需要装 Node、不需要管理员权限。
#       同时压缩为 dist\LevelForge-win-x64-<日期>.zip 便于分发。
#
# 用法（一般由 package.cmd 调用）：
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\package.ps1
# 可选参数：
#   -SkipZip     只生成 publish\，不打 zip
#   -NoSmoke     跳过冒烟自检
param(
    [switch]$SkipZip,
    [switch]$NoSmoke
)

$ErrorActionPreference = 'Stop'

# 注意：本文件必须保存为「UTF-8 带 BOM」，否则 Windows PowerShell 5.1 会把中文读成乱码。
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$publish = Join-Path $root 'publish'
$distDir = Join-Path $root 'dist'

# 共享环境探测（与 setup.ps1 同一套查找规则：用户目录的 10.x SDK / 便携 Node 也算数）
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
Write-Host "  LevelForge 发行包打包" -ForegroundColor White
Write-Host "  产物：publish\ + dist\*.zip（对方解压即用，零依赖）" -ForegroundColor DarkGray

$total = 6

# ---------------------------------------------------------------- 1) 工具链
Step 1 $total "检查工具链（打包机需要 .NET 10 SDK 与 Node.js）"
if (-not (Use-DotNet10)) {
    Die "没有可用的 .NET 10 SDK（当前 dotnet：$(Get-DotNetVersionText)）" "先在打包机上运行 setup.cmd（它会自动安装 .NET 10 SDK 与 Node.js）"
}
if (-not (Use-Node)) {
    Die "找不到 node" "运行 setup.cmd 自动安装 Node.js（或手动安装后重试）"
}
Ok "dotnet $(Get-DotNetVersionText) / node $(node -v)"

# ---------------------------------------------------------------- 2) 前端
Step 2 $total "构建界面（web\dist）"
Push-Location (Join-Path $root 'web')
try {
    if (-not (Test-Path 'node_modules')) {
        Write-Host "      首次构建：安装前端依赖..." -ForegroundColor Gray
        & npm install --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw "npm install 失败" }
    }
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build 失败" }
} catch {
    Pop-Location
    Die "$_" "先单独跑 build.cmd 看详细报错"
}
Pop-Location
Ok "界面产物：web\dist"

# ---------------------------------------------------------------- 3) 自包含发布
Step 3 $total "发布自包含后端（win-x64，已内置 .NET 运行时）"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
& dotnet publish src\LevelForge.Platform -c Release -r win-x64 --self-contained -o $publish --nologo
if ($LASTEXITCODE -ne 0) { Die "dotnet publish 失败" "把上面的报错发出来（首次发布需要联网下载 win-x64 运行时包）" }
Ok "publish\ 生成完成"

# ---------------------------------------------------------------- 4) 界面 + 插件
Step 4 $total "把界面与玩法插件装进发布目录"
Copy-Item (Join-Path $root 'web\dist') (Join-Path $publish 'wwwroot') -Recurse -Force
Ok "界面已嵌入 publish\wwwroot"

$adapterProj = 'adapters\piggyfarm\LevelForge.Adapters.PiggyFarm\LevelForge.Adapters.PiggyFarm.csproj'
& dotnet build $adapterProj -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { Die "玩法插件构建失败" "先单独构建 $adapterProj 看报错" }
$adapterDll = Join-Path $root 'adapters\piggyfarm\LevelForge.Adapters.PiggyFarm\bin\Release\net10.0\LevelForge.Adapters.PiggyFarm.dll'
$pluginDir = Join-Path $publish 'data\plugins\piggyfarm'
New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null
Copy-Item $adapterDll $pluginDir -Force
Ok "PiggyFarm 插件已放入 data\plugins\piggyfarm"

# ---------------------------------------------------------------- 5) 启动脚本 + 说明
Step 5 $total "写入启动脚本与使用说明"

$startCmd = @'
@echo off
rem LevelForge one-click start (self-contained: no .NET / Node needed)
setlocal EnableExtensions
cd /d "%~dp0"
if not exist "data" mkdir "data"

netstat -ano | findstr ":5000" | findstr "LISTENING" >nul 2>nul
if not errorlevel 1 (
  echo LevelForge is already running - opening http://localhost:5000
  start "" "http://localhost:5000"
  timeout /t 2 >nul
  exit /b 0
)

echo Starting LevelForge ... the browser will open at http://localhost:5000
echo Keep this window open. Close it (or press Ctrl+C) to stop LevelForge.
start "" /min powershell -NoProfile -WindowStyle Hidden -Command "Start-Sleep -Seconds 5; Start-Process 'http://localhost:5000'"
LevelForge.Platform.exe --urls http://localhost:5000
echo.
echo LevelForge stopped. Press any key to close.
pause >nul
'@
Set-Content -Path (Join-Path $publish 'start.cmd') -Value $startCmd -Encoding ASCII
Ok "start.cmd 已写入"

$readme = @'
LevelForge 使用说明
=====================================

一、第一次使用
  1. 双击本目录里的 start.cmd（如果 Windows 提示"已保护你的电脑"，点"更多信息 → 仍要运行"）
  2. 稍等几秒，浏览器会自动打开 http://localhost:5000
  3. 关闭时：关掉那个黑色窗口即可（窗口关掉，服务就停止）

二、配置 AI 出题（可选，但生成关卡需要）
  1. 打开左侧"设置"页
  2. 填 LLM 服务地址（OpenAI 兼容接口，例如 https://api.deepseek.com/v1）、API Key、模型名
  3. 保存后回到"生成"页试一次

三、数据存在哪
  - 全部在本目录的 data\ 子目录里（数据库、磁带、关卡、插件）
  - 换电脑：把整个文件夹拷走即可，不需要重新安装

四、常见问题
  - 打不开页面：确认黑色窗口还开着；端口 5000 被占用时先关掉占用它的程序
  - 想看自己的游戏能否接入：左侧"接入"向导，第一步选游戏工程目录
  - 本目录可以整体删除来卸载，不会在系统里留东西

五、系统要求
  - Windows 10/11 64 位；无需安装 .NET、Node.js 或任何运行库
'@
Set-Content -Path (Join-Path $publish '使用说明.txt') -Value $readme -Encoding UTF8
Ok "使用说明.txt 已写入"

# ---------------------------------------------------------------- 6) 冒烟自检 + 打包
Step 6 $total "冒烟自检（真的启动一次，确认接口与插件都在）"

if ($NoSmoke) {
    Warn "已跳过冒烟自检（-NoSmoke）"
}
else {
    $port = 5099
    $url = "http://127.0.0.1:$port"
    $exe = Join-Path $publish 'LevelForge.Platform.exe'
    $proc = Start-Process -FilePath $exe -ArgumentList '--urls', $url -WorkingDirectory $publish -PassThru -WindowStyle Hidden
    try {
        $ok = $false
        for ($i = 0; $i -lt 40; $i++) {
            Start-Sleep -Milliseconds 500
            try {
                $games = Invoke-RestMethod -Uri "$url/api/games" -TimeoutSec 2 -UseBasicParsing
                $ok = $true
                break
            } catch { }
        }
        if (-not $ok) { throw "启动后 20 秒内接口没有响应" }

        $ids = ($games | ForEach-Object { $_.gameId }) -join ','
        if ($ids -notmatch 'piggyfarm') { throw "接口可用，但没装载 PiggyFarm 插件（当前玩法：$ids）" }

        $html = Invoke-WebRequest -Uri "$url/" -TimeoutSec 5 -UseBasicParsing
        if ($html.Content -notmatch '<div id="root"') { throw "首页不是前端页面（wwwroot 可能没嵌进去）" }

        Ok "接口正常（玩法：$ids）"
        Ok "界面正常（首页返回前端 HTML）"
    } catch {
        Die "冒烟自检失败：$_" "检查 publish\ 是否完整；单独运行 publish\start.cmd 看报错"
    } finally {
        if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    }

    # 自检产生的运行数据（数据库/磁带）不要打进包里，只保留 plugins
    Get-ChildItem (Join-Path $publish 'data') -Force | Where-Object { $_.Name -ne 'plugins' } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not $SkipZip) {
    if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir | Out-Null }
    $stamp = Get-Date -Format 'yyyyMMdd'
    $zip = Join-Path $distDir "LevelForge-win-x64-$stamp.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Write-Host "      正在压缩（约 1 分钟）..." -ForegroundColor Gray
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
    $mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Ok "发行包：$zip（$mb MB）"
}

Write-Host ""
Write-Host "  打包完成。" -ForegroundColor Green
Write-Host "  分发给别人：把 publish\ 整个文件夹（或 dist\ 里的 zip）发给对方" -ForegroundColor White
Write-Host "  对方：解压 → 双击 start.cmd → 浏览器自动打开 http://localhost:5000" -ForegroundColor White
Write-Host ""
