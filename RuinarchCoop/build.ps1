param([string]$GameRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$ErrorActionPreference = 'Stop'
$gameRoot = $GameRoot
$managed = Join-Path $gameRoot 'Ruinarch_Data\Managed'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references = @('mscorlib', 'System', 'System.Core', 'System.IO.Compression',
    'System.IO.Compression.FileSystem', 'UnityEngine', 'UnityEngine.CoreModule',
    'UnityEngine.IMGUIModule', 'UnityEngine.InputLegacyModule', 'UnityEngine.UIModule',
    'UnityEngine.UI', 'Assembly-CSharp', 'Ruinarch.Modding')
$compilerArgs = @('/nologo', '/noconfig', '/target:library', '/optimize+', '/nostdlib+',
    ('/out:' + (Join-Path $PSScriptRoot 'RuinarchCoop.dll')))
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $managed ($reference + '.dll')) }
$compilerArgs += '/reference:' + (Join-Path $gameRoot 'Mods\0Harmony.dll')
foreach ($sourceFile in @('CoopMod.cs', 'Wire.cs')) { $compilerArgs += Join-Path $PSScriptRoot $sourceFile }
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'RuinarchCoop compilation failed.' }
Write-Output 'Built RuinarchCoop.dll against the installed game and mod loader.'
