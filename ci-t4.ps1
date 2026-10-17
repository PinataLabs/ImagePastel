# Regenerates the code produced by every T4 template (*.tt) in the repository, using
# dotnet-t4 from the local tool manifest. CI runs this and fails if the result differs from
# what is committed, so run it after you edit a template or one of its *.ttinclude files.
#
# Each output is written next to its template, with the extension from the template's
# output directive (".cs" when there is none, as in Visual Studio). Line endings follow
# the output's eol attribute in .gitattributes, so the result is the same on every OS.
# Outputs are UTF-8 without a BOM. A file is only written when its bytes change.
$ErrorActionPreference = 'Stop'

# The tool manifest and the git calls resolve from the current directory.
Push-Location $PSScriptRoot
$temp = Join-Path ([IO.Path]::GetTempPath()) "imagesharp-t4-$PID"

try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed' }

    # Tracked templates, and new ones that are not yet added but not ignored either.
    # Ignored build output is skipped, and git does not list files in nested worktrees.
    $templates = @(git ls-files --cached --others --exclude-standard -- '*.tt' |
        Sort-Object -Unique |
        Where-Object { Test-Path -LiteralPath (Join-Path $PSScriptRoot $_) })
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed' }

    $utf8 = [Text.UTF8Encoding]::new($false)
    $changed = 0
    $index = 0

    foreach ($template in $templates) {
        $templatePath = Join-Path $PSScriptRoot $template
        $text = [IO.File]::ReadAllText($templatePath)
        $extension = '.cs'
        if ($text -match '<#@\s*output\s[^#]*?extension\s*=\s*"([^"]+)"') {
            $extension = $Matches[1]
            if (-not $extension.StartsWith('.')) { $extension = ".$extension" }
        }

        $output = [IO.Path]::ChangeExtension($template, $extension)
        $eol = "`n"
        if ((git check-attr eol -- $output) -match ': eol: crlf$') { $eol = "`r`n" }

        # A new directory for each template, so output from one can never be read as
        # output of the next. dotnet-t4 applies the output directive's extension to -o,
        # so keep it the same.
        $index++
        $generatedDir = Join-Path $temp $index
        New-Item -ItemType Directory -Force $generatedDir | Out-Null
        $generated = Join-Path $generatedDir "output$extension"
        dotnet t4 $templatePath -o $generated
        if ($LASTEXITCODE -ne 0) { throw "dotnet t4 failed for $template" }
        if (-not (Test-Path -LiteralPath $generated)) { throw "dotnet t4 wrote no output for $template" }

        # Template text keeps the template's own line endings, while code written by the
        # template uses the platform's, so normalize all of them.
        $content = [IO.File]::ReadAllText($generated) -replace "`r?`n", $eol
        $bytes = $utf8.GetBytes($content)

        # Compare bytes, so a BOM or another encoding also counts as a change.
        $outputPath = Join-Path $PSScriptRoot $output
        if (-not (Test-Path -LiteralPath $outputPath) -or
            -not [Linq.Enumerable]::SequenceEqual([byte[]][IO.File]::ReadAllBytes($outputPath), [byte[]]$bytes)) {
            [IO.File]::WriteAllBytes($outputPath, $bytes)
            Write-Host "Updated $output"
            $changed++
        }
    }

    Write-Host "Processed $($templates.Count) templates, updated $changed."
}
finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -Recurse -Force $temp }
    Pop-Location
}
