param([string]$GameRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$ErrorActionPreference = 'Stop'
$gameRoot = $GameRoot
$managed = Join-Path $gameRoot 'Ruinarch_Data\Managed'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$references = @('mscorlib', 'System', 'System.Core', 'UnityEngine', 'UnityEngine.CoreModule',
    'UnityEngine.AudioModule', 'UnityEngine.JSONSerializeModule', 'UnityEngine.UnityWebRequestModule',
    'UnityEngine.UnityWebRequestAudioModule', 'Assembly-CSharp', 'Ruinarch.Modding', 'AK.Wwise.Unity.API')
$compilerArgs = @('/nologo', '/noconfig', '/target:library', '/optimize+', '/nostdlib+', ('/out:' + (Join-Path $PSScriptRoot 'GameplayMusic.dll')))
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $managed ($reference + '.dll')) }
$compilerArgs += '/reference:' + (Join-Path $gameRoot 'Mods\0Harmony.dll')
foreach ($sourceFile in @('MusicMod.cs', 'MusicLogic.cs', 'EventHooks.cs')) { $compilerArgs += Join-Path $PSScriptRoot $sourceFile }
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'GameplayMusic compilation failed.' }
Write-Output 'Built GameplayMusic.dll against the installed game and mod loader.'

