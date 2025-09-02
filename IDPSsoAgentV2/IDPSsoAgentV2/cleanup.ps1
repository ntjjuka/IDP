param(
  [int]$Port = 5400,
  [switch]$Aggressive,          # 루트CA/서버 인증서까지 삭제 (주의!)
  [string]$RootCN = "TrayAgent Root CA",
  [string]$LeafCN = "localhost",
  [string]$RunName = "TrayHttpsAgent",
  [string]$TaskName = "\IDPSsoAgent_Autostart"  # 과거 잘못 만든 예약 작업명 예시
)

$ErrorActionPreference = "SilentlyContinue"
function W($m){ Write-Host "[cleanup] $m" }

W "==== START cleanup for port $Port ===="

# 1) http.sys URLACL / SSL 바인딩 제거
W "Removing http.sys urlacl / sslcert ..."
& netsh http delete urlacl  url=https://localhost:$Port/        | Out-Null
& netsh http delete urlacl  url=https://127.0.0.1:$Port/        | Out-Null
& netsh http delete urlacl  url=https://+`:$Port/                | Out-Null
& netsh http delete urlacl  url=http://localhost:$Port/         | Out-Null
& netsh http delete urlacl  url=http://127.0.0.1:$Port/         | Out-Null

& netsh http delete sslcert ipport=0.0.0.0:$Port                 | Out-Null
& netsh http delete sslcert ipport=[::]:$Port                    | Out-Null

W "Current SSL bindings for port check:"
(netsh http show sslcert) -split "`r?`n" | Select-String -SimpleMatch ":$Port" | ForEach-Object { "   $_" }

# 2) 자동 시작 제거 (HKCU Run / 예약 작업)
W "Removing auto-start entries ..."
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -Path $runKey -Name $RunName -ErrorAction SilentlyContinue) {
  Remove-ItemProperty -Path $runKey -Name $RunName -Force
  W "Removed HKCU Run: $RunName"
}

# 예약 작업(있으면 삭제)
schtasks /Delete /TN $TaskName /F | Out-Null

# 패턴으로 남아있는 유사 작업도 정리 (선택)
Get-ScheduledTask | Where-Object { $_.TaskName -match 'IDP|Agent|TrayHttps' } | ForEach-Object {
  try { schtasks /Delete /TN ("\\" + $_.TaskName) /F | Out-Null; W "Removed task: $($_.TaskName)" } catch {}
}

# 3) 앱 마커파일/잠금 파일 정리(선택)
$pgData = Join-Path $env:ProgramData "TrayHttpsAgent"
$markers = @(
  "https_ready_$Port.marker",
  "https_setup_$Port.lock"
) | ForEach-Object { Join-Path $pgData $_ }

foreach ($f in $markers) { if (Test-Path $f) { Remove-Item $f -Force; W "Removed: $f" } }

# 4) 인증서 정리 (Aggressive 모드에서만)
if ($Aggressive) {
  W "Aggressive mode: Removing Root CA and Leaf certificates ... (주의: 다른 장비 신뢰도에 영향 없음 - 로컬만)"
  # LocalMachine\My 에서 CN=localhost (leaf) 제거
  $stores = @(
    @{Name="My";   Loc="LocalMachine"; Subject=$LeafCN},
    @{Name="My";   Loc="LocalMachine"; Subject=$RootCN}
  )

  foreach ($s in $stores) {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store($s.Name, $s.Loc)
    try {
      $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
      $toRemove = $store.Certificates | Where-Object { $_.Subject -eq "CN=$($s.Subject)" }
      foreach ($c in $toRemove) { $store.Remove($c); W "Removed $($s.Loc)\$($s.Name): $($c.Subject) [$($c.Thumbprint)]" }
    } catch {} finally { $store.Close() }
  }

  # Root 신뢰 스토어에서 RootCN 제거 (LM/CU)
  foreach ($loc in @("LocalMachine","CurrentUser")) {
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", $loc)
    try {
      $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
      $toRemove = $store.Certificates | Where-Object { $_.Subject -eq "CN=$RootCN" }
      foreach ($c in $toRemove) { $store.Remove($c); W "Removed $loc\\Root: $($c.Subject) [$($c.Thumbprint)]" }
    } catch {} finally { $store.Close() }
  }
}

# 5) http.sys 캐시 정리(선택)
# netsh http flush logbuffer | Out-Null  # 필요 시

W "==== DONE. Reboot not required. ===="
W "Tip: 재구성하려면 앱을 실행하거나(최초 1회 UAC 허용), 수동으로 다음을 확인하세요:"
W "  - netsh http show urlacl | findstr :$Port"
W "  - netsh http show sslcert | findstr :$Port"
