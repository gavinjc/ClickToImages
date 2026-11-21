# Builds the projects that need VS2022's own MSBuild.exe rather than dotnet: the shell extension
# (vcxproj) and the MSIX package (wapproj). The csproj projects build with plain `dotnet build`.
# Exists so the long quoted MSBuild path is one permission rule, and so every session builds the
# same way. Keep this file ASCII-only: Windows PowerShell 5 reads a BOM-less .ps1 as ANSI.
#
# Run as: powershell -ExecutionPolicy Bypass -File ./build.ps1 [-Target ShellExtension|Package]
#         [-Configuration Debug|Release] [-CertificateThumbprint <hash>]

param(
    [ValidateSet('ShellExtension', 'Package')]
    [string]$Target = 'Package',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    # Signs by a cert already in CurrentUser\My instead of the .pfx beside the wapproj, for a
    # password-protected .pfx whose password must not go in the repo (lessons.md -> Packaging).
    [string]$CertificateThumbprint
)

$ErrorActionPreference = 'Stop'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -version '[17.0,18.0)' -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
    Select-Object -First 1
if (-not $msbuild)
{
    throw 'VS2022 MSBuild.exe not found. The C++ desktop and UWP workloads are needed.'
}

$root = $PSScriptRoot
$shellExtension = Join-Path $root 'src\ClickToImages.ShellExtension\ClickToImages.ShellExtension.vcxproj'
$package = Join-Path $root 'src\ClickToImages.Package\ClickToImages.Package.wapproj'
$app = Join-Path $root 'src\ClickToImages.App\ClickToImages.App.csproj'

function Invoke-MSBuild([string[]]$arguments)
{
    & $msbuild @arguments -nologo -verbosity:minimal "-p:Configuration=$Configuration" '-p:Platform=x64'
    if ($LASTEXITCODE -ne 0)
    {
        exit $LASTEXITCODE
    }
}

if ($Target -eq 'ShellExtension')
{
    Invoke-MSBuild @($shellExtension, '-restore')
    exit 0
}

# The wapproj must never be restored itself (NU1201). Its references are restored here instead:
# the App for win-x64, or the wapproj build fails NETSDK1047 after a plain `dotnet build`.
dotnet restore $app -r win-x64
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}
Invoke-MSBuild @($shellExtension, '-t:Restore')

$packageArguments = @($package)
if ($CertificateThumbprint)
{
    $packageArguments += '-p:AppxPackageSigningEnabled=true'
    $packageArguments += "-p:PackageCertificateThumbprint=$CertificateThumbprint"
    $packageArguments += '-p:PackageCertificateKeyFile='
}
Invoke-MSBuild $packageArguments
