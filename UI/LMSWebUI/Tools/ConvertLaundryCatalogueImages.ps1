[CmdletBinding()]
param(
	[string]$ManifestPath,
	[string]$SourceRoot,
	[string]$DestinationRoot,
	[string]$ServiceType,
	[string]$Category
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
	$ManifestPath = Join-Path $PSScriptRoot '..\LMSWebUI\wwwroot\catalog\laundry-item-catalogue.json'
}
if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
	$SourceRoot = Join-Path $PSScriptRoot '..\LMSWebUI\wwwroot\catalog\laundry-items-source'
}
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
	$DestinationRoot = Join-Path $PSScriptRoot '..\LMSWebUI\wwwroot\catalog\laundry-items'
}

function Get-RelativeManifestPath {
	param([string]$ImagePath)

	$prefix = '/catalog/laundry-items/'
	if (-not $ImagePath.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) -or
		-not $ImagePath.EndsWith('.webp', [System.StringComparison]::OrdinalIgnoreCase)) {
		throw "Manifest imagePath is outside the expected catalogue root or is not WebP: $ImagePath"
	}

	return $ImagePath.Substring($prefix.Length).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function Invoke-ImageMagick {
	param([string[]]$Arguments)

	& magick @Arguments
	if ($LASTEXITCODE -ne 0) {
		throw "ImageMagick failed with exit code $LASTEXITCODE. Arguments: $($Arguments -join ' ')"
	}
}

function Test-ConvertedImage {
	param([string]$Path)

	if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
		return $false
	}

	$metadata = (& magick identify -format '%m|%w|%h' -- $Path 2>$null)
	if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($metadata)) {
		return $false
	}

	$parts = $metadata.Trim().Split('|')
	return $parts.Count -eq 3 -and
		$parts[0].Equals('WEBP', [System.StringComparison]::OrdinalIgnoreCase) -and
		$parts[1] -eq '400' -and
		$parts[2] -eq '400'
}

$ManifestPath = (Resolve-Path -LiteralPath $ManifestPath).Path
$SourceRoot = [System.IO.Path]::GetFullPath($SourceRoot)
$DestinationRoot = [System.IO.Path]::GetFullPath($DestinationRoot)

if (-not (Test-Path -LiteralPath $SourceRoot -PathType Container)) {
	throw "Source folder does not exist: $SourceRoot"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$items = @($manifest.items | Where-Object {
		([string]::IsNullOrWhiteSpace($ServiceType) -or $_.serviceType -eq $ServiceType) -and
		([string]::IsNullOrWhiteSpace($Category) -or $_.category -eq $Category)
	})
$allItems = @($manifest.items)
if ([string]::IsNullOrWhiteSpace($ServiceType) -and [string]::IsNullOrWhiteSpace($Category) -and $allItems.Count -ne 53) {
	throw "Expected 53 manifest entries, found $($allItems.Count). No conversion was started."
}
if ($items.Count -eq 0) {
	throw 'No manifest entries match the requested filters. No conversion was started.'
}

Write-Output "Manifest: $ManifestPath"
Write-Output "Source root: $SourceRoot"
Write-Output "Destination root: $DestinationRoot"
Write-Output "Entries selected: $($items.Count)"

$results = [System.Collections.Generic.List[object]]::new()
foreach ($item in $items) {
	$relativeWebpPath = Get-RelativeManifestPath -ImagePath ([string]$item.imagePath)
	$relativeSourcePath = [System.IO.Path]::Combine(
		[System.IO.Path]::GetDirectoryName($relativeWebpPath),
		[System.IO.Path]::GetFileNameWithoutExtension($relativeWebpPath))
	$sourceCandidates = @(@('.png', '.jpg', '.jpeg') | ForEach-Object {
		Join-Path $SourceRoot ($relativeSourcePath + $_)
	} | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })

	$destinationPath = Join-Path $DestinationRoot $relativeWebpPath
	Write-Output "[$($item.serviceType) / $($item.category) / $($item.itemName)]"
	Write-Output "  Source candidates: $($sourceCandidates -join ', ')"
	Write-Output "  Destination: $destinationPath"
	if ($sourceCandidates.Count -eq 0) {
		Write-Error "  ERROR: No PNG, JPG, or JPEG source found."
		$results.Add([pscustomobject]@{ Item = $item.itemName; Status = 'Missing source'; Path = $relativeSourcePath })
		continue
	}

	if ($sourceCandidates.Count -gt 1) {
		Write-Error "  ERROR: Multiple source files found."
		$results.Add([pscustomobject]@{ Item = $item.itemName; Status = 'Ambiguous source'; Path = ($sourceCandidates -join '; ') })
		continue
	}

	try {
		$destinationDirectory = Split-Path -Parent $destinationPath
		New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
		$destinationArgument = "webp:$destinationPath"
		Invoke-ImageMagick -Arguments @(
			$sourceCandidates[0],
			'-auto-orient',
			'-resize', '400x400^',
			'-gravity', 'center',
			'-extent', '400x400',
			'-strip',
			'-quality', '85',
			$destinationArgument
		)

		if (-not (Test-ConvertedImage -Path $destinationPath)) {
			throw "Output verification failed; expected WebP 400x400."
		}

		Write-Output '  SUCCESS: Converted and verified as WebP 400x400.'
		$results.Add([pscustomobject]@{ Item = $item.itemName; Status = 'Converted and verified'; Path = $relativeWebpPath })
	}
	catch {
		Write-Error "  ERROR: $($_.Exception.Message)"
		$results.Add([pscustomobject]@{ Item = $item.itemName; Status = 'Failed'; Path = $_.Exception.Message })
	}
}

$results | Format-Table -AutoSize
$missing = @($results | Where-Object Status -eq 'Missing source').Count
$ambiguous = @($results | Where-Object Status -eq 'Ambiguous source').Count
$failed = @($results | Where-Object Status -eq 'Failed').Count
$verified = @($results | Where-Object Status -eq 'Converted and verified').Count

Write-Output "Summary: $verified verified, $missing missing, $ambiguous ambiguous, $failed failed; total $($results.Count)."
if ($missing -or $ambiguous -or $failed) {
	throw 'Catalogue conversion did not complete successfully. No manifest values were changed.'
}

Write-Output "Manifest was not modified: $ManifestPath"
