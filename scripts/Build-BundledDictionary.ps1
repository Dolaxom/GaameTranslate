param([string]$SourcePath)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$expected = '7c48ef3981903361ea90f8ef9a64e58cc7899341d0fb873c2f61198198cba7af'
if (-not $SourcePath) {
    $SourcePath = Join-Path $root 'artifacts/dictionaries/eng-rus.tei'
    New-Item -ItemType Directory -Force (Split-Path $SourcePath) | Out-Null
    Invoke-WebRequest 'https://download.freedict.org/generated/eng-rus/eng-rus.tei' -OutFile $SourcePath
}
if ((Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Unexpected dictionary version. Review its data and license before updating the pinned hash.' }
$options = [Xml.XmlReaderSettings]::new()
$options.DtdProcessing = [Xml.DtdProcessing]::Ignore
$options.XmlResolver = $null
$reader = [Xml.XmlReader]::Create($SourcePath, $options)
$document = [Xml.XmlDocument]::new()
try { $document.Load($reader) } finally { $reader.Dispose() }
$ns = [Xml.XmlNamespaceManager]::new($document.NameTable)
$ns.AddNamespace('t', 'http://www.tei-c.org/ns/1.0')
function NormalizeDictionaryText([string]$text) {
    $normalized = $text.Normalize([Text.NormalizationForm]::FormD)
    return ([regex]::Replace($normalized, '[\u0301\u0341]', '')).Normalize([Text.NormalizationForm]::FormC).Trim()
}
$entries = [Collections.Generic.SortedDictionary[string,object]]::new([StringComparer]::Ordinal)
foreach ($entry in $document.SelectNodes('//t:entry', $ns)) {
    $words = @($entry.SelectNodes('t:form/t:orth', $ns) | ForEach-Object { (NormalizeDictionaryText $_.InnerText).ToLowerInvariant().Replace([char]0x2019, [char]0x27) })
    $meanings = @($entry.SelectNodes('.//t:cit[@type="trans" and @xml:lang="ru"]/t:quote', $ns) | ForEach-Object { NormalizeDictionaryText $_.InnerText } | Where-Object { $_ } | Select-Object -Unique)
    foreach ($word in $words) {
        if ($word -and $meanings.Count) {
            $plainSpelling = $entry.SelectSingleNode('t:form/t:orth', $ns).InnerText
            # Prefer ordinary lowercase entries over proper names merged into the same lookup key.
            $combined = if ($plainSpelling -ceq $plainSpelling.ToLowerInvariant()) { $meanings + @($entries[$word]) } else { @($entries[$word]) + $meanings }
            $entries[$word] = @($combined | Where-Object { $_ } | Select-Object -Unique)
        }
    }
}
$destination = Join-Path $root 'src/GaameTranslate.Infrastructure/Data/eng-rus.json.gz'
New-Item -ItemType Directory -Force (Split-Path $destination) | Out-Null
$json = ConvertTo-Json -InputObject $entries -Depth 4 -Compress
$file = [IO.File]::Create($destination)
$gzip = [IO.Compression.GZipStream]::new($file, [IO.Compression.CompressionLevel]::SmallestSize)
try { $bytes = [Text.Encoding]::UTF8.GetBytes($json); $gzip.Write($bytes, 0, $bytes.Length) } finally { $gzip.Dispose(); $file.Dispose() }
Write-Output "Bundled $($entries.Count) English headwords: $destination"
