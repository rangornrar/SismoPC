param([ValidateSet('stop','verify')][string]$Action)
$ErrorActionPreference = 'Stop'
try {
    $svc = Get-Service SismoPC -ErrorAction SilentlyContinue
    if ($Action -eq 'stop') {
        if ($null -eq $svc) { exit 0 }
        if ($svc.Status -ne 'Stopped') {
            Stop-Service SismoPC
            $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        }
        & "$env:SystemRoot\System32\sc.exe" delete SismoPC
        if ($LASTEXITCODE -ne 0) { throw 'Suppression du service refusée' }
        $svc.Dispose() # Fermer le handle avant d'attendre la suppression effective.
        $svc = $null
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while (Get-Service SismoPC -ErrorAction SilentlyContinue) {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Service encore marqué pour suppression : redémarrage nécessaire' }
            Start-Sleep -Milliseconds 250
        }
    } else {
        if ($null -eq $svc) { throw 'Service absent' }
        $svc.WaitForStatus('Running', [TimeSpan]::FromSeconds(30))
        $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', 'SismoPC', [IO.Pipes.PipeDirection]::InOut)
        try {
            $pipe.Connect(5000)
            $writer = [IO.StreamWriter]::new($pipe)
            $writer.AutoFlush = $true
            $reader = [IO.StreamReader]::new($pipe)
            $writer.WriteLine('{"type":"ping"}')
            $reply = $reader.ReadLineAsync()
            if (-not $reply.Wait(5000) -or $reply.Result -notmatch 'pong') { throw 'Service connecté sans réponse ping' }
        } finally { $pipe.Dispose() }
    }
    exit 0
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
