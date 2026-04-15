param(
    [ValidateSet("Debug", "Release")]
    [string]$Mode = "Release"
)

# 0. 터미널 인코딩 설정 (한글 깨짐 방지)
chcp 65001 >$null
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# 1. 배포 경로 설정 (프로젝트 루트 내 Dist 폴더)
$distPath = ".\Dist"
$editorDist = "$distPath\Editor"
$runtimeDist = "$distPath\Runtime\$Mode"

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "   Verity Engine 배포 패키지 생성 시작" -ForegroundColor Cyan
Write-Host "   모드: $Mode" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# 2. 기존 Dist 폴더 삭제 (클린 빌드)
if (Test-Path $distPath) {
    Write-Host ">> 기존 배포 폴더 정리 중..." -ForegroundColor Gray
    Remove-Item -Recurse -Force $distPath
}

# 3. 에디터(Editor) 빌드 및 게시
Write-Host ">> [1/4] 에디터(Editor) 빌드 중..." -ForegroundColor Yellow
# 네이티브 라이브러리 호환성을 위해 PublishSingleFile을 사용하지 않고 안정적인 폴더 배포를 선택합니다.
dotnet publish Editor/Verity.Editor.App/Verity.Editor.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $editorDist --nologo

# 4. 런타임(Runtime/Game Player) 빌드 및 게시
Write-Host ">> [2/4] 런타임 엔진(Runtime) 빌드 중..." -ForegroundColor Yellow
if ($Mode -eq "Debug") {
    dotnet publish Verity.Game/Verity.Game.csproj -c Debug -r win-x64 --self-contained true -p:PublishSingleFile=false -p:RuntimeShowConsole=true -p:RuntimeDiagnostics=true -p:DebugSymbols=true -p:DebugType=portable -o $runtimeDist --nologo
}
else {
    dotnet publish Verity.Game/Verity.Game.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:RuntimeShowConsole=false -p:RuntimeDiagnostics=false -p:DebugSymbols=false -p:DebugType=None -o $runtimeDist --nologo
}

# 5. 필수 리소스 복사 및 배포판 정제
Write-Host ">> [3/4] 배포판 리소스 정제 및 복사 중..." -ForegroundColor Yellow

# 에디터 언어 파일 복사
$localeSrc = "Editor/Verity.Editor/Locales"
if (Test-Path $localeSrc) {
    Copy-Item -Recurse -Force $localeSrc "$editorDist\Locales"
}

# 에디터 리소스(폰트 및 로고 이미지) 복사
$resourceSrc = "Editor/Verity.Editor/EditorResources"
if (Test-Path $resourceSrc) {
    Copy-Item -Recurse -Force $resourceSrc "$editorDist\EditorResources"
}

# 런타임 폴더 내의 개발용 테스트 에셋 제거 (Dist 폴더 내에서만 삭제됨)
# 배포판에는 엔진 구동에 필요한 최소한의 구조만 남깁니다.
$filesToRemove = Get-ChildItem -Path $runtimeDist -Include "*.png", "*.verity", "*.shader", "*.style", "*.blueprint", "scene.json" -Recurse
if ($filesToRemove) {
    $filesToRemove | Remove-Item -Force
}

# 빈 Assets 폴더 구조만 생성
if (-not (Test-Path "$runtimeDist\Assets")) {
    New-Item -ItemType Directory -Path "$runtimeDist\Assets" | Out-Null
}

# 6. 마무리 작업 (이름 변경 등)
Write-Host ">> [4/4] 마무리 작업 중..." -ForegroundColor Yellow

# 에디터 실행 파일 이름 변경
$oldExe = "$editorDist\Verity.Editor.App.exe"
if (Test-Path $oldExe) {
    Rename-Item $oldExe "VerityEditor.exe"
}

# 라이선스 및 리드미 복사 (있는 경우)
if (Test-Path "LICENSE") { Copy-Item "LICENSE" $distPath }
if (Test-Path "README.md") { Copy-Item "README.md" $distPath }

Write-Host "`n========================================" -ForegroundColor Green
Write-Host "   배포 패키지 생성 완료!" -ForegroundColor Green
Write-Host "   경로: $(Resolve-Path $distPath)" -ForegroundColor White
Write-Host "========================================`n" -ForegroundColor Green
