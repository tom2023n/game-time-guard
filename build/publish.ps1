$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "运行单元测试"
dotnet test GameTimeGuard.sln -c Release --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$publish = @(
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=none"
)

Write-Host "发布服务"
dotnet publish src/GameTimeGuard.Service/GameTimeGuard.Service.csproj @publish -o artifacts/publish/service
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "发布托盘程序"
dotnet publish src/GameTimeGuard.Tray/GameTimeGuard.Tray.csproj @publish -o artifacts/publish/tray
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$iscc = @(
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 7\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 7\ISCC.exe")
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) {
    Write-Host "未安装 Inno Setup，已跳过安装包。发布文件在 artifacts\publish。"
    exit 0
}

Write-Host "编译安装包"
& $iscc installer\GameTimeGuard.iss
exit $LASTEXITCODE
