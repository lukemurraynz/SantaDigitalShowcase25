# 🎅 Santa's Digital Elves – Wishlist Detection & Gift Report

Santa's Digital Elves is an event-driven demo application that detects wishlist and profile updates from children, processes them using real-time event streaming with [Drasi](https://drasi.io/), and generates intelligent "Naughty & Nice Gift Reports" using Azure AI.

## 🎯 What This Solution Does

- **Real-time Event Detection**: Uses Drasi for continuous query processing on event streams
- **Wishlist Management**: Children can submit and update their Christmas wishlists
- **AI-Powered Analysis**: Azure OpenAI generates gift recommendations and behavior insights
- **Modern Architecture**: .NET 9 backend, React frontend, Azure Container Apps, and Drasi on AKS

## 🛠️ Technology Stack

| Layer | Technology |
|-------|------------|
| **Backend** | C# / .NET 9 with ASP.NET Core Minimal APIs |
| **Frontend** | TypeScript with Vite + React |
| **Database** | Azure Cosmos DB (Core/NoSQL) |
| **Event Processing** | Drasi for real-time event detection, Azure Event Hubs |
| **AI Framework** | Azure OpenAI with Microsoft Agent Framework (.NET preview) |
| **Infrastructure** | Azure Container Apps, Azure Key Vault, AKS for Drasi |
| **Scripting** | PowerShell for deployment and automation |

## 📋 Prerequisites

Before you begin, ensure you have the following installed:

- **Azure CLI** (`az`) – [Install guide](https://docs.microsoft.com/cli/azure/install-azure-cli)
- **Azure Developer CLI** (`azd`) – [Install guide](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd)
- **.NET SDK 9** – [Download](https://dotnet.microsoft.com/download/dotnet/9.0)
- **Node.js 18+** – [Download](https://nodejs.org/)
- **Docker** (optional, for local container builds)
- **kubectl** – [Install guide](https://kubernetes.io/docs/tasks/tools/)
- **Drasi CLI** – [Install guide](https://drasi.io/docs/getting-started/installation/)

## 🚀 Quick Start Deployment

### 1. Clone and Navigate

```powershell
# Clone the repository (or copy this folder to your own repo)
cd SantaDigitalShowcae25
```

### 2. Authenticate with Azure

```powershell
az login
azd auth login
```

### 3. Create Environment and Deploy

```powershell
# Create a new azd environment
azd env new <your-environment-name>

# Deploy infrastructure and all services
azd up

# Verify deployment
.\scripts\test-demo-readiness.ps1
```

### 4. Access the Application

After deployment completes, access your application:

```powershell
# Get the application URL
$apiUrl = azd env get-value apiHost
Start-Process "https://$apiUrl"
```

## 🏗️ Project Structure

```
SantaDigitalShowcae25/
├── src/                    # .NET 9 backend API
│   ├── Middleware/         # ASP.NET Core middleware
│   ├── Realtime/           # Real-time event handling
│   ├── lib/                # Shared libraries
│   ├── models/             # Domain models and DTOs
│   ├── services/           # Business logic and API endpoints
│   └── Program.cs          # Application entry point
├── frontend/               # Vite + React frontend
│   ├── src/                # React components
│   └── public/             # Static assets
├── tests/                  # Test projects
│   ├── unit/               # xUnit unit tests
│   ├── integration/        # Integration tests
│   └── contract/           # Contract tests
├── drasi/                  # Drasi event graph configuration
│   ├── manifests/          # Kubernetes manifests for Drasi
│   └── resources/          # Drasi resource definitions
├── infra/                  # Bicep infrastructure as code
│   └── modules/            # Modular Bicep templates
├── scripts/                # PowerShell automation scripts
├── azure.yaml              # Azure Developer CLI configuration
├── Dockerfile.multi        # Multi-stage Docker build
└── SantaDigitalShowcae25.sln  # Visual Studio solution file
```

## 💻 Local Development

### Backend (.NET)

```powershell
# Restore dependencies
dotnet restore src/src.csproj

# Build the solution
dotnet build src/src.csproj

# Run the API locally (port 8080)
$env:ASPNETCORE_URLS = "http://localhost:8080"
dotnet run --project src

# Run tests
dotnet test tests/Tests.csproj
```

### Frontend (React/Vite)

```powershell
cd frontend
npm install
npm run dev      # Development server with hot reload
npm run build    # Production build
```

### Full Solution Build

```powershell
dotnet build SantaDigitalShowcae25.sln
dotnet test
```

## 🔌 API Endpoints

### Health Endpoints

| Endpoint | Purpose |
|----------|---------|
| `/healthz` | Liveness check |
| `/readyz` | Readiness check |
| `/api/pingz` | Diagnostics payload |

### Core API Endpoints (v1)

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/v1/children` | GET | List all children |
| `/api/v1/children/{id}` | GET | Get child details |
| `/api/v1/children/{id}/wishlist-items` | GET, POST | Manage wishlist items |
| `/api/v1/reports` | GET | List reports |
| `/api/v1/elf-agents/{agentId}/run` | POST | Run AI elf agent (SSE) |
| `/api/v1/drasi/insights` | GET | Get Drasi insights |
| `/api/v1/copilot/chat` | POST | Chat with AI (SSE) |

## ⚙️ Configuration

### Environment Variables

The application uses Azure Key Vault for secrets. Key configuration values:

| Variable | Description |
|----------|-------------|
| `KEYVAULT_URI` | Azure Key Vault URI |
| `COSMOS_ENDPOINT` | Cosmos DB endpoint |
| `OPENAI_ENDPOINT` | Azure OpenAI endpoint |
| `EVENTHUB_FQDN` | Event Hub namespace FQDN |

### Drasi Configuration

Drasi resources are managed via the Drasi CLI:

```powershell
# Set Drasi environment
drasi env kube -n drasi-system

# Apply Drasi resources
drasi apply -f drasi/manifests/drasi-resources.yaml
```

## 🔧 Troubleshooting

### Common Issues

1. **API returns 404**: Container App may still be using bootstrap image
   ```powershell
   azd deploy api
   ```

2. **Drasi pods in CrashLoopBackOff**: Run the fix script
   ```powershell
   $rg = azd env get-value AZURE_RESOURCE_GROUP
   $env = azd env get-value AZURE_ENV_NAME
   .\scripts\fix-drasi-deployment.ps1 -ResourceGroup $rg -Project "santadigitalshowcase" -Env $env
   ```

3. **Frontend shows 404 on API calls**: Ensure the Container App is properly deployed
   ```powershell
   azd deploy api
   ```

### Validation Script

Run the demo readiness check to validate your deployment:

```powershell
.\scripts\test-demo-readiness.ps1
```

## 📦 Deployment Options

### Azure Developer CLI (Recommended)

```powershell
azd up              # Full deployment
azd deploy api      # Deploy only backend
azd deploy drasi    # Deploy only Drasi resources
azd down            # Tear down all resources
```

### Manual Bicep Deployment

```powershell
$project = "santadigitalshowcase"
$env = "dev"
$rg = "${project}-${env}-rg"
$loc = "eastus"

az group create -n $rg -l $loc
az deployment group create -g $rg -f ./infra/main.bicep -p project=$project env=$env
```

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## 📄 License

This project is provided as a demo/sample application for the Festive Tech Calendar 2025.

## 🔗 Resources

- [Drasi Documentation](https://drasi.io/docs/)
- [Azure Container Apps](https://learn.microsoft.com/azure/container-apps/)
- [Azure Developer CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/)
- [.NET 9 Documentation](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9)
- [Festive Tech Calendar](https://festivetechcalendar.com/)
