param(
    [string]$GameDir = "D:\SteamLibrary\steamapps\common\Ultimate Admiral Dreadnoughts",
    [string]$CarrierDir = "D:\UADCarrier",
    [switch]$DryRun
)

# Merge UADCarrier overlays into live game files. All target files must have
# .carrier_bak backups (created separately). Append-only except aiPersonalities,
# which gets surgical quoted-field appends.

$ud = Join-Path $GameDir "Mods\Default_Files\UAD_Files"
$csv = Join-Path $CarrierDir "csv"
$errors = @()

function Append-Rows($overlayFile, $targetFile) {
    $ov = Get-Content (Join-Path $csv $overlayFile)
    # take only data rows: skip comments (#), header (@), blank
    $rows = @($ov | Where-Object { $_ -notmatch '^\s*#' -and $_ -notmatch '^\s*@' -and $_.Trim() -ne '' })
    if ($DryRun) { Write-Host "DRYRUN append $($rows.Count) rows from $overlayFile -> $targetFile"; return }
    $tp = Join-Path $ud $targetFile
    # Ensure the target ends with a newline so the first appended row lands on its own line.
    $raw = [System.IO.File]::ReadAllText($tp)
    if (-not $raw.EndsWith("`n")) { Add-Content -Path $tp -Value "" -NoNewline:$false }
    Add-Content -Path $tp -Value $rows -Encoding utf8
    Write-Host "appended $($rows.Count) rows: $overlayFile -> $targetFile"
}

# 1-4: append-only merges (hulls, tubes, ship type)
# NOTE (Option B): new tech groups/types/technologies are NOT added — the game
# ignores unknown tech lines from CSV. Carrier hulls are instead unlocked via
# existing hull_strength techs (see docs/HULL_UNLOCKS.md). The old
# techGroups/techTypes/technologies_carrier.csv overlays have been removed.
Append-Rows "torpedoTubes_override.csv" "torpedoTubes.csv"
Append-Rows "shipTypes_carrier.csv" "shipTypes.csv"
Append-Rows "parts_carrier.csv" "parts.csv"

# 7: aiPersonalities surgical merge
$aiMap = @{
    'japan_ai'   = @{ cv='1.2'; tech=@('aviation_deck;3','aviation_ops;3','aviation_strike;3') }
    'usa_ai'     = @{ cv='1.0'; tech=@('aviation_deck;3','aviation_ops;2','aviation_strike;2') }
    'britain_ai' = @{ cv='0.8'; tech=@('aviation_deck;2','aviation_ops;2') }
    'german_ai'  = @{ cv='0.5'; tech=@('aviation_deck;2') }
    'france_ai'  = @{ cv='0.4'; tech=@('aviation_deck;1') }
    'italy_ai'   = @{ cv='0.3'; tech=@('aviation_deck;1') }
    'russia_ai'  = @{ cv='0.3'; tech=@('aviation_deck;1') }
    'austria_ai' = @{ cv='0.2'; tech=@('aviation_deck;1') }
    'spain_ai'   = @{ cv='0.2'; tech=@('aviation_deck;1') }
    'china_ai'   = @{ cv='0.2'; tech=@('aviation_deck;1') }
}
$genericCv = '0.5'
$genericTech = @('aviation_deck;1')

$aiPath = Join-Path $ud "aiPersonalities.csv"
$lines = [System.IO.File]::ReadAllLines($aiPath)
$out = New-Object System.Collections.Generic.List[string]
$patched = 0

function SplitQ($line) {
    $r=@(); $inQ=$false; $cur=""
    foreach($ch in $line.ToCharArray()){
        if($ch -eq '"'){$inQ=-not $inQ; $cur+=$ch}
        elseif($ch -eq ',' -and -not $inQ){$r+=$cur; $cur=""}
        else{$cur+=$ch}
    }
    $r+=$cur; return $r
}

foreach ($line in $lines) {
    if ($line -match '^\s*#' -or $line -match '^\s*@' -or $line.Trim() -eq '' -or $line -match '^default') {
        $out.Add($line); continue
    }
    $f = SplitQ $line
    $name = $f[0]
    # aiParams is index 17 (quoted buildRatio/TechMod list)
    if ($f.Count -le 17) { $out.Add($line); continue }
    $params = $f[17]

    $cv = $null; $techs = $null
    if ($aiMap.ContainsKey($name)) { $cv = $aiMap[$name].cv; $techs = $aiMap[$name].tech }
    elseif ($name -match '_ai$') { $cv = $genericCv; $techs = $genericTech }
    else { $out.Add($line); continue }

    # skip if already patched (idempotent)
    if ($params -match 'buildRatio\(cv;') { $out.Add($line); continue }

    $add = "buildRatio(cv;$cv)"
    foreach ($t in $techs) { $add += ", TechMod($t)" }
    # $params may be quoted ("...") or bare (no commas, e.g. buildRatio(ss;0.25)).
    # Appending always introduces commas, so the result must be quoted.
    if ($params.StartsWith('"') -and $params.EndsWith('"') -and $params.Length -ge 2) {
        $inner = $params.Substring(1, $params.Length - 2)
        if ($inner.Trim() -eq '') { $newParams = '"' + $add + '"' }
        else { $newParams = '"' + $inner + ', ' + $add + '"' }
    } elseif ($params.Trim() -eq '') {
        $newParams = '"' + $add + '"'
    } else {
        $newParams = '"' + $params + ', ' + $add + '"'
    }
    $f[17] = $newParams
    $out.Add(($f -join ','))
    $patched++
}

if ($DryRun) { Write-Host "DRYRUN would patch $patched aiPersonalities rows" }
else {
    [System.IO.File]::WriteAllLines($aiPath, $out, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "patched $patched aiPersonalities rows"
}
if ($errors.Count -gt 0) { $errors | ForEach-Object { Write-Warning $_ } }
Write-Host "merge complete."
