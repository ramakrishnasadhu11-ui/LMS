$manifestPath = Join-Path $PSScriptRoot '..\..\UI\LMSWebUI\LMSWebUI\wwwroot\catalog\laundry-item-catalogue.json'
$manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json

$keys = @($manifest.items | ForEach-Object { "{0}|{1}|{2}" -f $_.serviceType, $_.category, $_.itemName })
$duplicates = $keys | Group-Object | Where-Object Count -gt 1
$invalid = @($manifest.items | Where-Object {
	$_.assetStatus -notin @('placeholder', 'approved') -or
	$_.imagePath -notmatch '^/catalog/laundry-items/.+/.+/.+\.webp$'
})

if ($duplicates.Count -gt 0) {
	throw "Duplicate catalogue item keys: $($duplicates.Name -join ', ')"
}

if ($invalid.Count -gt 0) {
	throw "Invalid catalogue entries: $($invalid.itemName -join ', ')"
}

"Validated $($manifest.items.Count) catalogue entries. All entries have unique keys and deterministic WebP paths."
"Placeholder entries: $(@($manifest.items | Where-Object assetStatus -eq 'placeholder').Count)"
"Approved entries: $(@($manifest.items | Where-Object assetStatus -eq 'approved').Count)"
