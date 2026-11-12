[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$taskModule = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskTools = Join-Path $taskModule '.tools'
$taskPackages = @(
    @{Name='BepInEx_win_x64_5.4.23.6.zip';Uri='https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.6/BepInEx_win_x64_5.4.23.6.zip';Hash='6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755';Folder='bepinex'},
    @{Name='bass24.zip';Uri='https://www.un4seen.com/files/bass24.zip';Hash='3A03EC9A33D0F4F9D167660DA51C8BB1432E8977496995455AB137277D69636E';Folder='bass24'},
    @{Name='basshls24.zip';Uri='https://www.un4seen.com/files/basshls24.zip';Hash='7531BC714DDE5A996D1C106B6F7AD08103611DE05BD38423D46A01711AF7E574';Folder='basshls24'}
)
foreach ($taskPackage in $taskPackages) {
    $taskZip = Join-Path $taskTools ('downloads\' + $taskPackage.Name)
    if (-not (Test-Path -LiteralPath $taskZip)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $taskZip) -Force | Out-Null
        Invoke-WebRequest -Uri $taskPackage.Uri -OutFile $taskZip -UseBasicParsing
    }
    if ((Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash -ne $taskPackage.Hash) { throw ('Pinned archive changed: ' + $taskPackage.Name + '. Review the upstream release before updating its hash.') }
    $taskFolder = Join-Path $taskTools $taskPackage.Folder
    if (-not (Test-Path -LiteralPath $taskFolder)) { Expand-Archive -LiteralPath $taskZip -DestinationPath $taskFolder }
}
Write-Output 'Pinned loader and audio dependencies are ready.'
