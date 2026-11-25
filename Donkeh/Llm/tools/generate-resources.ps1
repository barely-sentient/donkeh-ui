param(
    [string]$DocsDirectory = (Join-Path $PSScriptRoot '..\..\..\docs'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\Source\Resources')
)

# Regenerates the compact documentation and CSS metadata embedded in Donkeh.Llm.
# Run from the repository root after changing files under docs/.

$ErrorActionPreference = 'Stop'
$docsDirectory = [System.IO.Path]::GetFullPath($DocsDirectory)
$outputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

function Decode-Html([string]$text) {
    if ($null -eq $text) { return '' }
    return [System.Net.WebUtility]::HtmlDecode($text)
}

function Strip-Tags([string]$html) {
    if ($null -eq $html) { return '' }
    $text = $html
    $text = [regex]::Replace($text, '<br\s*/?>', "`n", 'IgnoreCase')
    $text = [regex]::Replace($text, '</(p|li|h[1-6]|tr|div|pre|blockquote)>', "`n", 'IgnoreCase')
    $text = [regex]::Replace($text, '<li[^>]*>', '- ', 'IgnoreCase')
    $text = [regex]::Replace($text, '<[^>]+>', '')
    $text = Decode-Html $text
    $lines = $text -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }
    return ($lines -join "`n")
}

function First-Match([string]$text, [string]$pattern, [int]$group = 1) {
    $found = [regex]::Match($text, $pattern, 'Singleline,IgnoreCase')
    if ($found.Success) { return $found.Groups[$group].Value }
    return ''
}

$topics = @()
$files = Get-ChildItem -Path $docsDirectory -Filter '*.html' -File | Sort-Object Name

foreach ($file in $files) {
    if ($file.Name -in @('404.html', 'index.html')) { continue }

    $html = [System.IO.File]::ReadAllText($file.FullName)
    $main = First-Match $html '<main[^>]*class="content"[^>]*>(.*?)</main>' 1
    if ([string]::IsNullOrWhiteSpace($main)) {
        $main = First-Match $html '<main[^>]*>(.*?)</main>' 1
    }
    if ([string]::IsNullOrWhiteSpace($main)) { continue }

    $main = [regex]::Replace($main, '<footer[^>]*>.*?</footer>', '', 'Singleline,IgnoreCase')
    $main = [regex]::Replace($main, '<div[^>]*class="page-nav"[^>]*>.*?</div>\s*$', '', 'Singleline,IgnoreCase')

    $title = Strip-Tags (First-Match $main '<h1[^>]*>(.*?)</h1>' 1)
    if ([string]::IsNullOrWhiteSpace($title)) { $title = $file.BaseName }
    $summary = Strip-Tags (First-Match $main '<p[^>]*class="lead"[^>]*>(.*?)</p>' 1)

    $sections = @()
    $h2Matches = [regex]::Matches($main, '<h2(?<attrs>[^>]*)>(?<title>.*?)</h2>', 'Singleline,IgnoreCase')

    for ($i = 0; $i -lt $h2Matches.Count; $i++) {
        $h2 = $h2Matches[$i]
        $start = $h2.Index + $h2.Length
        $end = if ($i + 1 -lt $h2Matches.Count) { $h2Matches[$i + 1].Index } else { $main.Length }
        $body = $main.Substring($start, $end - $start)
        $id = First-Match $h2.Groups['attrs'].Value 'id="([^"]+)"' 1
        if ([string]::IsNullOrWhiteSpace($id)) { $id = "section-$($i + 1)" }

        $codes = @()
        $codeBlockMatches = [regex]::Matches($body, '<div[^>]*class="code"[^>]*data-label="(?<label>[^"]*)"[^>]*>(?<inner>.*?)</div>', 'Singleline,IgnoreCase')
        foreach ($codeBlockMatch in $codeBlockMatches) {
            $label = Decode-Html $codeBlockMatch.Groups['label'].Value
            $inner = $codeBlockMatch.Groups['inner'].Value
            $preMatches = [regex]::Matches($inner, '<pre(?<attrs>[^>]*)><code>(?<code>.*?)</code></pre>', 'Singleline,IgnoreCase')

            foreach ($preMatch in $preMatches) {
                $code = Decode-Html $preMatch.Groups['code'].Value
                $code = $code.Trim("`r", "`n")
                $classMatch = [regex]::Match($preMatch.Groups['attrs'].Value, 'class="([^"]+)"', 'IgnoreCase')
                $language = 'text'
                if ($classMatch.Success) {
                    $language = $classMatch.Groups[1].Value.ToLowerInvariant()
                } elseif ($label -match '\.(cs|cx)$') { $language = 'csharp' }
                elseif ($label -match '\.css$') { $language = 'css' }
                elseif ($label -match '\.json$') { $language = 'json' }
                elseif ($label -match 'Terminal|PowerShell|Shell') { $language = 'shell' }
                elseif ($label -match '\.html$') { $language = 'html' }

                $codes += [ordered]@{ label = $label; language = $language; content = $code }
            }
        }

        $bodyWithoutCode = [regex]::Replace($body, '<div[^>]*class="code"[^>]*>.*?</div>', '', 'Singleline,IgnoreCase')
        $text = Strip-Tags $bodyWithoutCode

        $sections += [ordered]@{
            id = $id
            title = Strip-Tags $h2.Groups['title'].Value
            text = $text
            code = $codes
        }
    }

    $apiSignatures = @()
    foreach ($code in ($sections | ForEach-Object { $_.code })) {
        if ($code.language -eq 'csharp' -and $code.content -match '\b(public|protected|internal)\b') {
            foreach ($line in ($code.content -split "`n")) {
                if ($line -match '^\s*(public|protected|internal)\s+') {
                    $apiSignatures += $line.Trim()
                }
            }
        }
    }

    $examples = @()
    foreach ($section in $sections) {
        foreach ($code in $section.code) {
            if ($code.language -eq 'csharp' -and $examples.Count -lt 4) {
                $examples += [ordered]@{ section = $section.id; label = $code.label; code = $code.content }
            }
        }
    }

    $topics += [ordered]@{
        topic = $file.BaseName
        title = $title
        summary = $summary
        reference = "docs/$($file.Name)"
        sections = $sections
        apiSignatures = @($apiSignatures | Select-Object -Unique)
        examples = $examples
    }
}

$docsJson = [ordered]@{
    generatedFrom = 'docs/*.html'
    topics = $topics
}
[System.IO.File]::WriteAllText(
    (Join-Path $outputDirectory 'docs.json'),
    ($docsJson | ConvertTo-Json -Depth 12 -Compress),
    [System.Text.UTF8Encoding]::new($false))

# CSS property support table.
$cssHtml = [System.IO.File]::ReadAllText((Join-Path $docsDirectory 'css-support.html'))
$properties = @()
$rows = [regex]::Matches($cssHtml, '<tr><td><code>(?<name>[^<]+)</code></td><td>(?<status>.*?)</td><td>(?<used>.*?)</td>', 'Singleline,IgnoreCase')
foreach ($row in $rows) {
    $statusHtml = $row.Groups['status'].Value
    $statusText = Strip-Tags $statusHtml
    $note = ''
    $noteMatch = [regex]::Match($statusHtml, '<span[^>]*class="table-note"[^>]*>(?<note>.*?)</span>', 'Singleline,IgnoreCase')
    if ($noteMatch.Success) { $note = Strip-Tags $noteMatch.Groups['note'].Value }
    $status = if ($statusText -match '^Partial') { 'partial' } elseif ($statusText -match '^Not supported') { 'unsupported' } else { 'supported' }
    $properties += [ordered]@{
        name = Decode-Html $row.Groups['name'].Value
        status = $status
        notes = $note
        usedBy = Strip-Tags $row.Groups['used'].Value
    }
}

$propertiesJson = [ordered]@{
    source = 'docs/css-support.html'
    count = $properties.Count
    properties = $properties
}
[System.IO.File]::WriteAllText(
    (Join-Path $outputDirectory 'css-properties.json'),
    ($propertiesJson | ConvertTo-Json -Depth 6 -Compress),
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Wrote $($topics.Count) docs topics and $($properties.Count) CSS properties to $outputDirectory"
