param(
    [Parameter(Mandatory = $false)]
    [string]$PackagesRoot = (Join-Path $PSScriptRoot "..\packages"),

    [Parameter(Mandatory = $false)]
    [string]$ModRoot = (Join-Path $PSScriptRoot "..\rimworld-mods\AI_TranslationCore")
)

$ErrorActionPreference = "Stop"

# Each game version ships its own managed provider (matched to its runtime) plus
# the same native SQLite library. 1.6 uses net471 (Mono / .NET Framework), 1.7 uses
# netstandard2.0 (.NET 10 / Waylith).
$versionTargets = @(
    @{ VersionFolder = "1.6"; ManagedLib = "net471" },
    @{ VersionFolder = "1.7"; ManagedLib = "netstandard2.0" }
)

$nativeFiles = @(
    @{ Rid = "win-x64"; Source = "SourceGear.sqlite3.3.53.4\runtimes\win-x64\native\e_sqlite3.dll"; Name = "e_sqlite3.dll" },
    @{ Rid = "linux-x64"; Source = "SourceGear.sqlite3.3.53.4\runtimes\linux-x64\native\libe_sqlite3.so"; Name = "libe_sqlite3.so" },
    @{ Rid = "osx-x64"; Source = "SourceGear.sqlite3.3.53.4\runtimes\osx-x64\native\libe_sqlite3.dylib"; Name = "libe_sqlite3.dylib" },
    @{ Rid = "osx-arm64"; Source = "SourceGear.sqlite3.3.53.4\runtimes\osx-arm64\native\libe_sqlite3.dylib"; Name = "libe_sqlite3.dylib" }
)

# Check every input before changing either version's package.
foreach ($target in $versionTargets) {
    $target.ManagedSource = Join-Path $PackagesRoot ("System.Data.SQLite.2.0.4\lib\" + $target.ManagedLib + "\System.Data.SQLite.dll")
    if (-not (Test-Path -LiteralPath $target.ManagedSource -PathType Leaf)) {
        throw "Missing restored System.Data.SQLite provider for $($target.ManagedLib): $($target.ManagedSource)"
    }
}

foreach ($native in $nativeFiles) {
    $native.SourcePath = Join-Path $PackagesRoot $native.Source
    if (-not (Test-Path -LiteralPath $native.SourcePath -PathType Leaf)) {
        throw "Missing restored SQLite native runtime for $($native.Rid): $($native.SourcePath)"
    }
}

foreach ($target in $versionTargets) {
    $assemblyTarget = Join-Path $ModRoot ($target.VersionFolder + "\Assemblies\System.Data.SQLite.dll")
    $assemblyDirectory = Split-Path -Parent $assemblyTarget
    New-Item -ItemType Directory -Path $assemblyDirectory -Force | Out-Null
    Copy-Item -LiteralPath $target.ManagedSource -Destination $assemblyTarget -Force

    foreach ($native in $nativeFiles) {
        $targetDirectory = Join-Path $ModRoot ($target.VersionFolder + "\Native\sqlite\" + $native.Rid)
        New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
        $nativeTarget = Join-Path $targetDirectory $native.Name
        Copy-Item -LiteralPath $native.SourcePath -Destination $nativeTarget -Force
        $hash = (Get-FileHash -LiteralPath $nativeTarget -Algorithm SHA256).Hash.ToLowerInvariant()
        Set-Content -LiteralPath ($nativeTarget + ".sha256") -Value $hash -Encoding ascii -NoNewline
    }

    Write-Output "SQLite runtime assets prepared under $ModRoot\$($target.VersionFolder)"
}
