Param()
Write-Host "Ensuring Dapr installed (helm) with mTLS disabled" -ForegroundColor Cyan
if (-not (kubectl get ns dapr-system -o name 2>$null)) {
  helm repo add dapr https://dapr.github.io/helm-charts | Out-Null
  helm repo update | Out-Null
  helm install dapr dapr/dapr --version 1.14.5 `
    --namespace dapr-system `
    --create-namespace `
    --set global.registry=docker.io/daprio `
    --set dapr_operator.watchInterval=10s `
    --set global.mtls.enabled=false `
    --wait
}
else {
  Write-Host "dapr-system namespace exists; ensuring mTLS disabled via upgrade" -ForegroundColor Yellow
  helm upgrade dapr dapr/dapr --version 1.14.5 `
    --namespace dapr-system `
    --set global.registry=docker.io/daprio `
    --set dapr_operator.watchInterval=10s `
    --set global.mtls.enabled=false `
    --wait
}
Write-Host "Dapr predeploy step complete" -ForegroundColor Green
