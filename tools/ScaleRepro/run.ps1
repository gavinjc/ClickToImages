# Opens one ScaleRepro window. Drag it to the other screen, use a control, and back; see
# docs/PROGRESS.md M7.
#   powershell -File tools\ScaleRepro\run.ps1                            # baseline
#   powershell -File tools\ScaleRepro\run.ps1 -Variant stockmanifest     # one build-time variant
#   powershell -File tools\ScaleRepro\run.ps1 palette                    # one round-1 switch on
#   powershell -File tools\ScaleRepro\run.ps1 all -Release               # Release build
#   powershell -File tools\ScaleRepro\run.ps1 -RealApp                   # the real app, unpackaged Debug
#   powershell -File tools\ScaleRepro\run.ps1 -SdkVersion 2.7.0          # any SDK version, self-contained
# Variants (each changes one thing from the baseline, or says what it combines):
#   baseline, nomanifest, stockmanifest, selfcontained, sdk-1.5, sdk-1.5-selfcontained,
#   sdk-2.6.4, sdk-2.6.4-selfcontained, sdk-2.5, sdk-2.5-selfcontained
# Switches (round 1, baseline SDK only): palette darktitle icon messagebar addbutton all
# Switches (round 4, any SDK): mica desktopacrylic customtitle relayout resizenudge
# Join switches with '-' to name a combination: mica-customtitle, mica-palette
# Without PositionalBinding off, a bare first argument such as "mica-customtitle" binds to -Variant
# and fails its ValidateSet; this way only -Switches takes bare words.
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$Switches = @(),
    [ValidateSet('baseline', 'nomanifest', 'stockmanifest', 'selfcontained', 'sdk-1.5', 'sdk-1.5-selfcontained',
                 'sdk-2.6.4', 'sdk-2.6.4-selfcontained', 'sdk-2.5', 'sdk-2.5-selfcontained')]
    [string]$Variant = 'baseline',
    # Any 2.x or later Windows App SDK on nuget.org, built self-contained so it runs without
    # installing that runtime; replaces -Variant. For retesting the scale bug on each new release.
    [string]$SdkVersion,
    [switch]$Release,
    [switch]$RealApp
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')

# 1.5.240227000 is the only other Windows App SDK in the local NuGet cache. It has no separate
# Runtime package, and framework-dependent it needs the 1.5 Windows App Runtime installed.
$variantProperties = @{
    'baseline'              = @()
    'nomanifest'            = @('-p:ReproManifest=none')
    'stockmanifest'         = @('-p:ReproManifest=stock')
    'selfcontained'         = @('-p:ReproSelfContained=true')
    'sdk-1.5'               = @('-p:ReproSdkVersion=1.5.240227000', '-p:ReproRuntimePackage=false')
    'sdk-1.5-selfcontained' = @('-p:ReproSdkVersion=1.5.240227000', '-p:ReproRuntimePackage=false',
                                '-p:ReproSelfContained=true')
    # 2.6.4 is the newest stable 2.x on nuget.org (2026-10-29). 2.5.4-experimental is the line WinUI 3
    # Gallery 3.0.4.0 runs on (it reports Windows App Runtime 2.5.4.0). Framework-dependent, each
    # needs its own Windows App Runtime installed; the self-contained ones carry it in the output.
    'sdk-2.6.4'                = @('-p:ReproSdkVersion=2.6.4')
    'sdk-2.6.4-selfcontained'  = @('-p:ReproSdkVersion=2.6.4', '-p:ReproSelfContained=true')
    'sdk-2.5'                  = @('-p:ReproSdkVersion=2.5.4-experimental')
    'sdk-2.5-selfcontained'    = @('-p:ReproSdkVersion=2.5.4-experimental', '-p:ReproSelfContained=true')
}

if ($RealApp)
{
    $project = Join-Path $repo 'src\ClickToImages.App\ClickToImages.App.csproj'
    $configuration = 'Debug'
    $exe = Join-Path $repo 'src\ClickToImages.App\bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\ClickToImages.App.exe'
    $buildArguments = @()
}
else
{
    $project = Join-Path $PSScriptRoot 'ScaleRepro.csproj'
    $configuration = if ($Release) { 'Release' } else { 'Debug' }
    # A separate name: assigning to $Variant would re-run its ValidateSet.
    if ($SdkVersion)
    {
        $variantName = "sdk-$SdkVersion-selfcontained"
        $properties = @("-p:ReproSdkVersion=$SdkVersion", '-p:ReproSelfContained=true')
    }
    else
    {
        $variantName = $Variant
        $properties = $variantProperties[$Variant]
    }

    $exe = Join-Path $PSScriptRoot "bin\$variantName\x64\$configuration\net8.0-windows10.0.19041.0\win-x64\ScaleRepro.exe"
    $buildArguments = @("-p:ReproVariant=$variantName") + $properties
}

dotnet build $project -c $configuration -p:Platform=x64 @buildArguments --nologo -v quiet
if ($LASTEXITCODE -ne 0)
{
    throw "Build failed: $project $buildArguments"
}

if ($Switches.Count -gt 0)
{
    Start-Process -FilePath $exe -ArgumentList $Switches -WorkingDirectory (Split-Path $exe)
}
else
{
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)
}
