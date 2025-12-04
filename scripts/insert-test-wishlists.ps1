param(
    [string]$ResourceGroup = "rg-sdu",
    [string]$AccountName = "santaworkshop-sdu-cosmos",
    [string]$DatabaseName = "elves_demo",
    [string]$ContainerName = "wishlists",
    [int]$Count = 20
)

Write-Host "📝 Inserting $Count test wishlist records into Cosmos DB..." -ForegroundColor Cyan

# Sample items with varying frequencies
$items = @("Teddy Bear", "Train Set", "Lego Castle", "Bicycle", "Drone", "PS5", "Nintendo Switch", "Art Set", "Science Kit", "Robot Kit")
$children = @("elf-holly-45", "elf-ivy-12", "elf-snowbell-89", "elf-frost-23", "elf-ginger-67")

$now = [DateTime]::UtcNow

for ($i = 0; $i -lt $Count; $i++) {
    $childId = $children[$i % $children.Count]
    $item = $items[$i % $items.Count]
    $createdAt = $now.AddMinutes(-($i * 2)).ToString("o")  # Spread over last hour
    
    # Cosmos DB SQL API document format
    $doc = @{
        id = "wishlist-test-$i"
        ChildId = $childId
        Text = $item
        CreatedAt = $createdAt
        SchemaVersion = "v1"
        EventType = "WishlistSubmitted"
    } | ConvertTo-Json -Compress

    Write-Host "  → $childId wants $item (at $createdAt)" -ForegroundColor DarkGray
    
    # Insert using Azure CLI
    az cosmosdb sql container create-item `
        --account-name $AccountName `
        --database-name $DatabaseName `
        --container-name $ContainerName `
        --resource-group $ResourceGroup `
        --body $doc `
        --output none 2>&1 | Out-Null
}

Write-Host "✅ Inserted $Count test wishlist records successfully!" -ForegroundColor Green
Write-Host "   Database: $DatabaseName, Container: $ContainerName" -ForegroundColor DarkGray
