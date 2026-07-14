<#
.SYNOPSIS
Testet den Jellyfin Media Uploader Plugin API-Endpunkt durch das Hochladen einer oder mehrerer Dateien.

.DESCRIPTION
Sendet eine POST-Anfrage mit einer oder mehreren Dateien (multipart/form-data) an den
angegebenen Jellyfin Media Uploader Plugin Endpunkt. Baut den Body manuell auf
für bessere Kompatibilität mit verschiedenen PowerShell-Versionen.

.PARAMETER FilePaths
Ein oder mehrere vollständige Pfade zu den hochzuladenden Mediendateien.

.PARAMETER Destination
(Optional) Relativer Zielpfad unterhalb des im Plugin konfigurierten Basispfads
(z.B. "movies" oder "shows/Meine Serie/Staffel 1").

.PARAMETER JellyfinUrl
Die Basis-URL deiner Jellyfin Instanz (z.B. "http://localhost:8096").

.PARAMETER ApiKey
Dein Jellyfin API-Schlüssel zur Authentifizierung.

.EXAMPLE
.\MediaUpload.ps1 -FilePaths "C:\pfad\film.mkv","C:\pfad\film2.mkv" -Destination "movies"

.EXAMPLE
.\MediaUpload.ps1 -FilePaths "C:\pfad\episode.mkv" -Destination "shows/Meine Serie/Staffel 1" -JellyfinUrl "http://192.168.1.100:8096"
#>
param(
    [Parameter(Mandatory=$true)]
    [string[]]$FilePaths,

    [Parameter(Mandatory=$false)]
    [string]$Destination = "",

    [Parameter(Mandatory=$false)]
    [string]$JellyfinUrl = "http://localhost:8096",

    [Parameter(Mandatory=$false)]
    [string]$ApiKey = "f5e9519d0d9b42649d77b76a82d8e46f"
)

# Ziel-URL zusammenbauen
$uploadUrl = "$($JellyfinUrl.TrimEnd('/'))/Plugins/MediaUploader/Upload"

# Prüfen, ob alle Dateien existieren
foreach ($fp in $FilePaths) {
    if (-not (Test-Path -Path $fp -PathType Leaf)) {
        Write-Error "Datei nicht gefunden: $fp"
        return # Skript beenden
    }
}

# Header vorbereiten
$headers = @{}
if (-not [string]::IsNullOrEmpty($ApiKey)) {
    $headers.Add("X-Emby-Token", $ApiKey)
    Write-Host "Verwende API Key zur Authentifizierung."
} else {
    Write-Host "Versuche Upload ohne API Key."
}

if (-not [string]::IsNullOrEmpty($Destination)) {
    Write-Host "Ziel (relativ): $Destination"
}

Write-Host "Versuche $($FilePaths.Count) Datei(en) nach '$uploadUrl' hochzuladen..."

# --- Multipart/form-data Body manuell erstellen ---
$boundary = "---------------------------$([System.Guid]::NewGuid().ToString())"
$contentType = "multipart/form-data; boundary=$boundary"
$LF = "`r`n" # Zeilenumbruch für HTTP

$bodyBytes = [System.Collections.Generic.List[byte]]::new()

# 1) Das "destination" Feld anhängen (falls gesetzt)
if (-not [string]::IsNullOrEmpty($Destination)) {
    $destHeader = @(
        "--$boundary",
        "Content-Disposition: form-data; name=`"destination`"",
        "",
        $Destination
    ) -join $LF
    $bodyBytes.AddRange([System.Text.Encoding]::UTF8.GetBytes($destHeader + $LF))
}

# 2) Jede Datei als eigenen "files" Part anhängen
foreach ($fp in $FilePaths) {
    $fileItem = Get-Item -Path $fp
    try {
        $fileBytes = [System.IO.File]::ReadAllBytes($fp)
    } catch {
         Write-Error "Fehler beim Lesen der Datei '$fp': $($_.Exception.Message)"
         return
    }

    # MIME-Typ bestimmen (Basis-Erkennung, kann verbessert werden)
    $mimeType = switch ($fileItem.Extension.ToLower()) {
        ".mkv"  { "video/x-matroska" }
        ".mp4"  { "video/mp4" }
        ".avi"  { "video/x-msvideo" }
        ".mov"  { "video/quicktime" }
        ".wmv"  { "video/x-ms-wmv" }
        ".ts"   { "video/mp2t" }
        ".webm" { "video/webm" }
        ".mp3"  { "audio/mpeg" }
        ".flac" { "audio/flac" }
        ".wav"  { "audio/wav" }
        default { "application/octet-stream" } # Standard-Binärtyp
    }

    $fileHeader = @(
        "--$boundary",
        # Der Parametername 'files' muss mit dem im C# Controller übereinstimmen
        "Content-Disposition: form-data; name=`"files`"; filename=`"$($fileItem.Name)`"",
        "Content-Type: $mimeType",
        ""
    ) -join $LF

    $bodyBytes.AddRange([System.Text.Encoding]::UTF8.GetBytes($fileHeader + $LF))
    $bodyBytes.AddRange($fileBytes)
    $bodyBytes.AddRange([System.Text.Encoding]::UTF8.GetBytes($LF))
}

# 3) Abschließendes Boundary anhängen
$bodyBytes.AddRange([System.Text.Encoding]::UTF8.GetBytes("--$boundary--" + $LF))

$finalBody = $bodyBytes.ToArray()
# --- Ende Body-Erstellung ---


# Stoppuhr für die Zeitmessung
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    $response = Invoke-RestMethod -Uri $uploadUrl -Method Post -Headers $headers -ContentType $contentType -Body $finalBody

    $stopwatch.Stop()
    Write-Host "`n--- Server Antwort (Dauer: $($stopwatch.Elapsed.TotalSeconds)s) ---"
    $response | ConvertTo-Json -Depth 5 | Write-Host
    Write-Host "----------------------------------"
    Write-Host "Upload Befehl erfolgreich gesendet (Status 2xx). Prüfe Server-Logs und Dateisystem!" -ForegroundColor Green

} catch {
    $stopwatch.Stop()
    Write-Error "Fehler während der Web-Anfrage (Dauer: $($stopwatch.Elapsed.TotalSeconds)s):"
    Write-Error $_.Exception.Message

    # Versuche, Statuscode und Antwort aus der Exception zu extrahieren
    $statusCode = $null
    $errorContent = $null
    if ($_.Exception.Response) {
         try { $statusCode = [int]$_.Exception.Response.StatusCode } catch {}
         try {
             $stream = $_.Exception.Response.GetResponseStream()
             $reader = New-Object System.IO.StreamReader($stream)
             $errorContent = $reader.ReadToEnd()
         } catch {
             $errorContent = "Fehlerinhalt konnte nicht gelesen werden."
         }
    }
    if ($statusCode) { Write-Error "HTTP Status Code: $statusCode" }
    if ($errorContent) { Write-Error "Fehler Antwort Inhalt: $errorContent" }
}

Write-Host "`nSkript beendet."
