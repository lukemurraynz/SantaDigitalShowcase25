using './main.bicep'

// NOTE: env is automatically aligned with the current azd environment via azure.yaml parameters
param env = readEnvironmentVariable('AZURE_ENV_NAME', 'dev')
param project = 'santadigitalshowcase'

// ⚠️ BOOTSTRAP IMAGE - Temporary placeholder for initial infrastructure deployment
// The actual application image will be built and deployed by 'azd deploy api'
// This bootstrap image allows 'azd provision' to complete without requiring a pre-built app image
// After 'azd up' completes, the Container App will automatically use the real image from ACR
param apiImage = 'mcr.microsoft.com/k8se/quickstart:latest'
param acrEnableAdminUser = true
// Optional override of OpenAI account & model
// Make OpenAI account name environment-specific to avoid global custom subdomain conflicts
param openaiModelName = 'gpt-4o-mini'
// Prefer Standard SKU with minimal capacity to avoid quota issues
param openaiDeploymentSkuName = 'Standard'
param openaiDeploymentCapacity = 5
// Use default/latest model version to maximize availability
param openaiModelVersion = ''
