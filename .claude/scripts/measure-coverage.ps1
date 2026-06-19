# Local coverage measurement for the pr-crew/coverage gate scope.
# Runs Claude.Core.Tests on the Windows TFM with coverlet.msbuild, excluding
# generated code + live-OS Win32 glue, then prints the scoped line coverage
# and per-file uncovered lines. Mirrors what CI measures + posts.
param([switch]$Quiet)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $repo

Get-ChildItem Claude.Core.Tests -Filter 'coverage.cobertura*.xml' -ErrorAction SilentlyContinue | Remove-Item -Force

$dashP = '/p:'
$args = @(
  'Claude.Core.Tests/Claude.Core.Tests.csproj',
  '-f', 'net10.0-windows10.0.26100.0', '-c', 'Debug',
  "${dashP}CollectCoverage=true",
  "${dashP}CoverletOutput=coverage.cobertura.xml"
)
$testOut = & dotnet test @args 2>&1
if (-not $Quiet) { $testOut | Select-String -Pattern 'Passed!|Failed!|^\| (Module|Claude|Total)' }

$xml = (Get-ChildItem Claude.Core.Tests -Filter 'coverage.cobertura*.xml' | Select-Object -First 1).FullName
$py = @"
import xml.etree.ElementTree as ET
r = ET.parse(r'$xml').getroot()
tot = cov = 0
rows = []
for cls in r.iter('class'):
    fn = cls.get('filename'); lines = cls.find('lines')
    ll = lines.findall('line') if lines is not None else []
    c = sum(1 for x in ll if int(x.get('hits')) > 0)
    tot += len(ll); cov += c
    miss = [x.get('number') for x in ll if int(x.get('hits')) == 0]
    if ll and c < len(ll):
        rows.append((c / len(ll), fn, c, len(ll), miss))
pct = round(100.0 * cov / tot, 2) if tot else 0.0
print(f'SCOPED line coverage: {cov}/{tot} = {pct}%')
rows.sort()
for lr, fn, c, nl, miss in rows:
    print(f'{lr*100:5.1f}%  {c:3d}/{nl:3d}  {fn}  miss:[{",".join(miss[:30])}]')
"@
$py | python -