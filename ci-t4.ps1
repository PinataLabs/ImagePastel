# Regenerates the code produced by every T4 template (*.tt) in the repository, using
# dotnet-t4 from the local tool manifest. CI runs this and fails if the result differs from
# what is committed, so run it after you edit a template or one of its *.ttinclude files.
#
# Each output is written next to its template, with the extension from the template's
# output directive (".cs" when there is none, as in Visual Studio). Line endings follow
# the output's eol attribute in .gitattributes, so the result is the same on every OS.
# A file is only written when its content changes.
$ErrorActionPreference = 'Stop'

dotnet tool restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Only tracked templates, so build output and nested worktrees are left alone.
$templates = @(git -C $PSScriptRoot ls-files -- '*.tt')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$temp = Join-Path ([IO.Path]::GetTempPath()) "imagesharp-t4-$PID"
New-Item -ItemType Directory -Force $temp | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$changed = 0

try {
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
        if ((git -C $PSScriptRoot check-attr eol -- $output) -match ': eol: crlf$') { $eol = "`r`n" }

        # dotnet-t4 applies the output directive's extension to -o, so keep it the same.
        $generated = Join-Path $temp "output$extension"
        dotnet t4 $templatePath -o $generated
        if ($LASTEXITCODE -ne 0) { throw "dotnet t4 failed for $template" }

        # Template text keeps the template's own line endings, while code written by the
        # template uses the platform's, so normalize all of them.
        $content = [IO.File]::ReadAllText($generated) -replace "`r?`n", $eol
        $outputPath = Join-Path $PSScriptRoot $output
        if (-not (Test-Path -LiteralPath $outputPath) -or [IO.File]::ReadAllText($outputPath) -cne $content) {
            [IO.File]::WriteAllText($outputPath, $content, $utf8)
            Write-Host "Updated $output"
            $changed++
        }
    }
}
finally {
    Remove-Item -Recurse -Force $temp
}

Write-Host "Processed $($templates.Count) templates, updated $changed."
