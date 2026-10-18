# Compila CatturaSchermo.exe usando il compilatore C# incluso in Windows (nessuna installazione necessaria).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$csc = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$refs = '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll'

# 0) chiude CatturaSchermo se è in esecuzione, altrimenti l'exe non si può sovrascrivere
Get-Process CatturaSchermo -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

# 1) compila una prima volta (senza icona) per generare il file .ico dal codice
$ico = Join-Path $PSScriptRoot 'CatturaSchermo.ico'
if (-not (Test-Path $ico)) {
    $tmp = Join-Path $env:TEMP 'CatturaSchermo_icon.dll'
    & $csc /nologo /target:library /out:$tmp $refs CatturaSchermo.cs
    if ($LASTEXITCODE -ne 0) { throw "Compilazione fallita" }
    $asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($tmp))
    $asm.GetType('CatturaSchermo.IconArt').GetMethod('SaveIco').Invoke($null, [object[]]@([string]$ico)) | Out-Null
    Remove-Item $tmp
}

# 2) compila l'eseguibile finale
& $csc /nologo /target:winexe /optimize+ /out:CatturaSchermo.exe /win32icon:CatturaSchermo.ico $refs CatturaSchermo.cs
if ($LASTEXITCODE -ne 0) { throw "Compilazione fallita" }
Write-Output "Creato: $(Join-Path $PSScriptRoot 'CatturaSchermo.exe')"
