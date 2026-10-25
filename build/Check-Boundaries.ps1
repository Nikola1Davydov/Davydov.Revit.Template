#Requires -Version 7
<#
.SYNOPSIS
    Checks the project boundaries of the plan (docs/PLAN.md, sections 3.1 and 7).

.DESCRIPTION
    - References: every project in source/ may reference only the projects and packages of its row in table 3.1;
      a new project fails until it gets a row here.
    - Davydov.Revit.Hosting is headless: no UseWPF / UseWindowsForms, no System.Windows, no MessageBox.
    - Davydov.Revit.Addin: Features/ and Services/ use no System.Windows (UI goes through IUserNotifier and the
      frontend) and no ActiveUIDocument (the document goes through ActiveDocument). Exceptions:
      Services/Documents/ActiveDocument.cs and a line marked "// AllowActiveDocument: <reason>".
    - Davydov.Revit.Addin.Launcher uses no types of the add-in or the modules: they would load into Revit's
      context and bypass the isolation (decision 26). Names in strings, as in PluginInfo, are fine.

    Prints every violation as "file:line: rule" and exits with 1 when there is any.

.EXAMPLE
    pwsh build/Check-Boundaries.ps1
#>
param(
    [string] $Root = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'

# Table 3.1: what each project may reference. Packages are wildcard patterns.
$Common = @('JetBrains.Annotations', 'Polyfill')
$RevitApi = @('Nice3point.Revit.Api.*')
$Rules = [ordered]@{
    'Davydov.Revit.Hosting' = @{
        Projects = @()
        Packages = $RevitApi + $Common + @('Nice3point.Revit.Toolkit', 'Nice3point.Revit.Extensions', 'Microsoft.Extensions.*')
        Headless = $true
    }
    'Davydov.Revit.Hosting.WebView' = @{
        Projects = @('Davydov.Revit.Hosting')
        Packages = $RevitApi + $Common + @('Nice3point.Revit.Toolkit', 'Microsoft.Web.WebView2', 'Microsoft.Extensions.*')
    }
    'Davydov.Revit.Addin.ServiceDefaults' = @{
        Projects = @('Davydov.Revit.Hosting')
        Packages = $RevitApi + $Common + @('Nice3point.Revit.Toolkit', 'Nice3point.Revit.Logging', 'Microsoft.Extensions.*', 'Serilog.*')
    }
    'Davydov.Revit.Addin.Launcher' = @{
        Projects = @('Davydov.Revit.Addin')
        Packages = $RevitApi
    }
    'Davydov.Revit.Addin' = @{
        Projects = @('Davydov.Revit.Addin.ServiceDefaults', 'Davydov.Revit.Hosting', 'Davydov.Revit.Hosting.WebView')
        Packages = $RevitApi + $Common + @('Nice3point.Revit.Toolkit', 'Nice3point.Revit.Extensions', 'Microsoft.Extensions.*')
    }
}

$violations = [System.Collections.Generic.List[string]]::new()

function Add-Violation([string] $Path, [int] $Line, [string] $Message) {
    $relative = [System.IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
    $violations.Add("${relative}:${Line}: $Message")
}

# Code of a line without comments and string literals, so names in them do not count
function Get-Code([string] $Line) {
    $code = $Line -replace '@?"(?:[^"\\]|\\.)*"', '""'
    $comment = $code.IndexOf('//')
    if ($comment -ge 0) { $code = $code.Substring(0, $comment) }
    return $code
}

# Line-by-line check of the C# files under $Folder, skipping bin/obj
function Test-Code([string] $Folder, [string] $Pattern, [string] $Message, [scriptblock] $Allow = { $false }) {
    if (-not (Test-Path $Folder)) { return }

    Get-ChildItem $Folder -Recurse -Filter *.cs -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        ForEach-Object {
            $file = $_
            $number = 0
            foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
                $number++
                if ((Get-Code $line) -match $Pattern -and -not (& $Allow $file $line)) {
                    Add-Violation $file.FullName $number $Message
                }
            }
        }
}

# References
$projects = Get-ChildItem (Join-Path $Root 'source') -Recurse -Filter *.csproj -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|node_modules)[\\/]' }

foreach ($project in $projects) {
    $name = $project.BaseName
    if (-not $Rules.Contains($name)) {
        Add-Violation $project.FullName 1 "the project has no row in build/Check-Boundaries.ps1 (plan, table 3.1)"
        continue
    }

    $rule = $Rules[$name]
    [xml] $xml = Get-Content $project.FullName -Raw

    foreach ($reference in $xml.SelectNodes('//ProjectReference')) {
        $target = [System.IO.Path]::GetFileNameWithoutExtension($reference.Include)
        if ($target -notin $rule.Projects) {
            Add-Violation $project.FullName 1 "$name must not reference the project $target (plan, table 3.1)"
        }
    }

    foreach ($reference in $xml.SelectNodes('//PackageReference')) {
        $package = $reference.Include
        if (-not ($rule.Packages | Where-Object { $package -like $_ })) {
            Add-Violation $project.FullName 1 "$name must not reference the package $package (plan, table 3.1)"
        }
    }

    if ($rule.Headless) {
        foreach ($property in 'UseWPF', 'UseWindowsForms') {
            if ($xml.SelectSingleNode("//$property")?.InnerText -eq 'true') {
                Add-Violation $project.FullName 1 "$name is headless: no $property (plan, table 3.1)"
            }
        }
    }
}

# Code
$source = Join-Path $Root 'source'
$noWpf = '\bSystem\.Windows\b|\bMessageBox\b'

Test-Code (Join-Path $source 'Davydov.Revit.Hosting') $noWpf `
    'Davydov.Revit.Hosting is headless: no System.Windows or MessageBox (plan, table 3.1)'

foreach ($folder in 'Features', 'Services') {
    $path = Join-Path $source "Davydov.Revit.Addin/$folder"

    Test-Code $path $noWpf `
        "no System.Windows in $folder/: UI goes through IUserNotifier and the frontend (plan, table 3.1)"

    Test-Code $path '\bActiveUIDocument\b' `
        "no ActiveUIDocument in ${folder}/: take the document from ActiveDocument, or mark the line with // AllowActiveDocument: <reason> (plan, section 7)" `
        { param($file, $line) $file.Name -eq 'ActiveDocument.cs' -or $line -match '//\s*AllowActiveDocument:\s*\S' }
}

Test-Code (Join-Path $source 'Davydov.Revit.Addin.Launcher') '\bDavydov\.Revit\.(Hosting|Addin(?!\.Launcher\b))\b' `
    'the Launcher must not use types of the add-in or the modules: they would load outside its AssemblyLoadContext (decision 26)'

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host $_ }
    Write-Host "Project boundaries: $($violations.Count) violation(s)"
    exit 1
}

Write-Host "Project boundaries: OK ($($projects.Count) projects)"
