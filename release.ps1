# 빌드 → 시험 → 설치판(Inno Setup) → 포터블 zip → SHA256SUMS.txt
# 로컬과 GitHub Actions(.github/workflows/release.yml)가 같은 스크립트를 쓴다.
#   사용: powershell -ExecutionPolicy Bypass -File release.ps1
# 결과: dist\DefenderTray-<버전>-setup.exe, dist\DefenderTray-<버전>-portable.zip, dist\SHA256SUMS.txt
param([string]$Iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe")

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Step($name, [scriptblock]$body) {
    Write-Host "== $name" -ForegroundColor Cyan
    & $body
}

Step 'build' { cmd /c "`"$root\build.bat`""; if ($LASTEXITCODE -ne 0) { throw 'build.bat 실패' } }
Step 'test'  { cmd /c "`"$root\test.bat`"";  if ($LASTEXITCODE -ne 0) { throw 'test.bat 실패' } }

$full = (Get-Item "$root\DefenderTray.exe").VersionInfo.FileVersion
$version = ($full -split '\.')[0..2] -join '.'
Write-Host "version $version"

$dist = Join-Path $root 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

Step 'installer' {
    if (-not (Test-Path $Iscc)) { throw "Inno Setup 6 (ISCC.exe)이 없습니다: $Iscc" }
    & $Iscc "/DAppVersion=$version" "$root\installer\DefenderTray.iss" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'ISCC 실패' }
}

Step 'portable' {
    $p = Join-Path $dist 'portable'
    New-Item -ItemType Directory $p | Out-Null
    Copy-Item "$root\DefenderTray.exe", "$root\README.md", "$root\README.en.md", "$root\LICENSE" $p
    Compress-Archive -Path "$p\*" -DestinationPath "$dist\DefenderTray-$version-portable.zip" -Force
    Remove-Item $p -Recurse -Force
}

Step 'checksums' {
    Get-ChildItem $dist -File | Where-Object { $_.Extension -in '.exe', '.zip' } | Sort-Object Name |
        ForEach-Object { '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower(), $_.Name } |
        Set-Content "$dist\SHA256SUMS.txt" -Encoding ascii
    Get-Content "$dist\SHA256SUMS.txt" | Out-Host
}

# GitHub Actions에서 다음 단계가 쓸 버전
if ($env:GITHUB_OUTPUT) { "version=$version" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8 }
Write-Host "완료: $dist" -ForegroundColor Green
