# Speaks one line with an installed Windows voice and writes it as a WAV file.
# Runs entirely offline through Windows.Media.SpeechSynthesis, so the transcript never leaves the machine.
param(
    [Parameter(Mandatory = $true)][string]$Voice,
    [Parameter(Mandatory = $true)][string]$TextFile,
    [Parameter(Mandatory = $true)][string]$OutFile,
    [double]$Rate = 1.0,
    [double]$Pitch = 1.0
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.DataReader, Windows.Storage.Streams, ContentType = WindowsRuntime]

function Wait-Async($operation, [Type]$resultType) {
    $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() |
        Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
            $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } |
        Select-Object -First 1
    $task = $asTask.MakeGenericMethod($resultType).Invoke($null, @($operation))
    $task.Wait() | Out-Null
    return $task.Result
}

$synth = New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer
$match = [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices | Where-Object { $_.DisplayName -eq $Voice } | Select-Object -First 1
if ($null -eq $match) {
    throw "Voice '$Voice' is not installed."
}

$synth.Voice = $match
$synth.Options.SpeakingRate = $Rate
$synth.Options.AudioPitch = $Pitch

$text = [System.IO.File]::ReadAllText($TextFile, [System.Text.Encoding]::UTF8)
$stream = Wait-Async ($synth.SynthesizeTextToStreamAsync($text)) ([Windows.Media.SpeechSynthesis.SpeechSynthesisStream])
$size = [uint32]$stream.Size
$reader = New-Object Windows.Storage.Streams.DataReader($stream.GetInputStreamAt(0))
$null = Wait-Async ($reader.LoadAsync($size)) ([uint32])
$bytes = New-Object byte[] $size
$reader.ReadBytes($bytes)
[System.IO.File]::WriteAllBytes($OutFile, $bytes)
