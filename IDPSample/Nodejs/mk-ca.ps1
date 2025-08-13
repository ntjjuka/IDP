<# 
  Usage:
    pwsh ./make-dev-ca.ps1 -DnsName @("localhost","127.0.0.1") -OutDir .\certs -PfxPassword "change-me"

  Notes:
    - PowerShell 7+ 권장 (PEM까지 자동 생성)
    - Windows PowerShell 5.1에서도 실행되지만, PEM 키 변환은 OpenSSL 한 줄 필요 (아래 안내)
#>

param(
  [string[]]$DnsName = @("localhost","127.0.0.1"),
  [string]$OutDir = ".\certs",
  [string]$RootSubject = "CN=DEV Root CA",
  [string]$LeafSubject = "CN=DEV Leaf",
  [string]$PfxPassword = "change-me"
)

# 스크립트 파일이 있는 폴더 절대경로
$ScriptRoot = $PSScriptRoot
if (-not $ScriptRoot) {
  # (드물게) $PSScriptRoot가 비는 환경용 백업
  $ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
}

# OutDir를 스크립트 기준 절대경로로 강제
if (-not [System.IO.Path]::IsPathRooted($OutDir)) {
  $OutDir = Join-Path $ScriptRoot $OutDir
}

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null




Write-Host "== 1) 루트 CA 생성 (100년) =="
$root = New-SelfSignedCertificate -Type Custom `
  -Provider "Microsoft Enhanced RSA and AES Cryptographic Provider" `
  -KeySpec Signature `
  -KeyUsage CertSign,CRLSign,DigitalSignature `
  -KeyLength 2048 -KeyAlgorithm RSA -HashAlgorithm sha256 `
  -Subject $RootSubject `
  -KeyExportPolicy Exportable `
  -NotAfter (Get-Date).AddYears(100) `
  -TextExtension '2.5.29.19={critical}{text}ca=1&pathlength=1' `
  -CertStoreLocation "Cert:\CurrentUser\My"

# 루트 CA를 신뢰 저장소에 추가 (현재 사용자)
$rootCer = Join-Path $OutDir "rootCA.cer"
Export-Certificate -Cert $root -FilePath $rootCer | Out-Null
Import-Certificate -FilePath $rootCer -CertStoreLocation "Cert:\CurrentUser\Root" | Out-Null
Write-Host "  - 루트 CA 저장: $rootCer (신뢰 저장소에도 등록)"

Write-Host "== 2) 서버용(leaf) 인증서 생성 (100년, SAN 포함) =="
$leaf = New-SelfSignedCertificate -Type Custom `
  -Provider "Microsoft Enhanced RSA and AES Cryptographic Provider" `
  -KeySpec Signature `
  -DnsName $DnsName `
  -Signer $root `
  -KeyUsage DigitalSignature,KeyEncipherment `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1") `
  -Subject $LeafSubject `
  -NotAfter (Get-Date).AddYears(100) `
  -KeyExportPolicy Exportable `
  -CertStoreLocation "Cert:\CurrentUser\My"
  
# CER/PFX로 내보내기
$leafCer = Join-Path $OutDir "leaf-cert.cer"
Export-Certificate -Cert $leaf -FilePath $leafCer | Out-Null

$pfxPath = Join-Path $OutDir "leaf.pfx"
$sec = ConvertTo-SecureString $PfxPassword -AsPlainText -Force
Export-PfxCertificate -Cert $leaf -FilePath $pfxPath -Password $sec | Out-Null

Write-Host "  - 리프 CER: $leafCer"
Write-Host "  - 리프 PFX: $pfxPath"

Write-Host "== 3) PEM 파일 생성 (PowerShell 7+ 권장) =="
$psMajor = $PSVersionTable.PSVersion.Major
$leafPem = Join-Path $OutDir "leaf-cert.pem"
$leafKeyPem = Join-Path $OutDir "leaf-key.pem"
$chainPem = Join-Path $OutDir "chain.pem"
$fullchainPem = Join-Path $OutDir "fullchain.pem"

# CER -> PEM (인증서 텍스트)
function Convert-CerToPem([string]$cerPath, [string]$pemPath) {
  $bytes = [System.IO.File]::ReadAllBytes($cerPath)
  $base64 = [System.Convert]::ToBase64String($bytes) -split "(.{1,64})" | ? { $_ -and $_ -ne "" }
  @(
    "-----BEGIN CERTIFICATE-----"
    $base64
    "-----END CERTIFICATE-----"
  ) | Set-Content -NoNewline:$false -Encoding ascii -Path $pemPath
}

Convert-CerToPem $leafCer $leafPem
Convert-CerToPem $rootCer $chainPem

# PFX -> PEM(개인키)
if ($psMajor -ge 7) {
  try {
    $flags = [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable `
           -bor [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet
    $x = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($pfxPath, $PfxPassword, $flags)
    $rsa = $x.GetRSAPrivateKey()
    if (-not $rsa) { throw "RSAPrivateKey를 얻지 못했습니다." }

    # PKCS#8 (unencrypted) DER
    $pkcs8 = $rsa.ExportPkcs8PrivateKey()
    $b64 = [System.Convert]::ToBase64String($pkcs8) -split "(.{1,64})" | ? { $_ -and $_ -ne "" }
    @(
      "-----BEGIN PRIVATE KEY-----"
      $b64
      "-----END PRIVATE KEY-----"
    ) | Set-Content -NoNewline:$false -Encoding ascii -Path $leafKeyPem

    # fullchain = leaf + root
    Get-Content $leafPem, $chainPem | Set-Content -Encoding ascii $fullchainPem

    Write-Host "  - PEM 키 : $leafKeyPem"
    Write-Host "  - PEM cert: $leafPem"
    Write-Host "  - chain   : $chainPem"
    Write-Host "  - fullchain: $fullchainPem"
    Write-Host "완료 ✅ (PowerShell 7+ 경로)"
  }
  catch {
    Write-Warning "PEM 키 생성 실패(PowerShell 7 필요): $($_.Exception.Message)"
    Write-Host "아래 OpenSSL 명령으로 PFX→PEM 변환 가능:"
    Write-Host "  openssl pkcs12 -in `"$pfxPath`" -nocerts -nodes -out `"$leafKeyPem`" -password pass:$PfxPassword"
  }
}
else {
  Write-Warning "현재 PowerShell $psMajor. PFX→PEM 키 변환은 OpenSSL 필요."
  Write-Host "실행 예:"
  Write-Host "  openssl pkcs12 -in `"$pfxPath`" -nocerts -nodes -out `"$leafKeyPem`" -password pass:$PfxPassword"
  # fullchain은 cert들만 조합
  Get-Content $leafPem, $chainPem | Set-Content -Encoding ascii $fullchainPem
  Write-Host "  - fullchain: $fullchainPem"
}
