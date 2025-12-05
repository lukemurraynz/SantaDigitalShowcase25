# Script to remove AAA comments from test files
$testFiles = Get-ChildItem -Path "d:\GitHub\SantaDigitalWorkshop25\tests" -Filter "*.cs" -Recurse

foreach ($file in $testFiles) {
    $content = Get-Content $file.FullName -Raw
    $originalContent = $content
    
    # Remove standalone "// Arrange" comments
    $content = $content -replace '(?m)^\s*// Arrange\s*$\r?\n', ''
    
    # Remove standalone "// Act" comments  
    $content = $content -replace '(?m)^\s*// Act\s*$\r?\n', ''
    
    # Remove standalone "// Assert" comments
    $content = $content -replace '(?m)^\s*// Assert\s*$\r?\n', ''
    
    # Only write if content changed
    if ($content -ne $originalContent) {
        Set-Content -Path $file.FullName -Value $content -NoNewline
        Write-Host "Updated: $($file.Name)" -ForegroundColor Green
    }
}

Write-Host "AAA comments removed from test files" -ForegroundColor Cyan
