# 📚 Santa Digital Workshop Guides

This directory contains comprehensive guides for understanding, deploying, and using the Santa Digital Workshop system.

## 🚀 Quick Start Guides

### [Quick Start: Enhanced Agent Capabilities](quickstart-enhanced-agents.md)
Get started with multi-agent orchestration, streaming responses, and tool calling.
- Multi-agent collaborative recommendations
- Real-time streaming responses
- Available agent tools
- Testing instructions

### [Quick Start: Naughty/Nice Letter System](quickstart-naughty-nice.md)
Learn how to use the behavior tracking system with real-time status updates.
- Gift request submissions
- Behavior change detection
- Status verification
- Interactive demo usage

## 🏗️ Architecture & Integration

### [Drasi + Agent Framework Integration](drasi-agent-tool-integration.md)
How agents query real-time event patterns using Drasi continuous queries.
- 4 powerful Drasi query tools
- Tool calling architecture
- Demo endpoints
- Technical implementation details

### [Drasi Public Endpoint Setup](drasi-public-endpoint-setup.md)
Automated setup for exposing Drasi view service with public LoadBalancer.
- Automated deployment process
- Verification steps
- Integration testing

## 📊 Features

### [Year-over-Year Trends Feature](year-over-year-trends-feature.md)
Dashboard component comparing current trending toys with historical data.
- Backend API endpoints
- Frontend components
- Data source integration
- Demo value

## 📖 Reference & Planning

### [Naughty/Nice Story](naughty-nice-story.md)
Story-driven architecture for the letter to North Pole system.
- System overview
- Architecture components
- Data flow
- Testing scenarios

### [Enhancement Roadmap](enhancement-roadmap.md)
10 high-impact enhancements for showcasing Agent Framework capabilities.
- Multi-agent orchestration
- Streaming responses
- Advanced tool integration
- Production readiness features

## 🔧 Common Tasks

### Testing Locally
\\\powershell
# Run the API
dotnet run --project src

# Use interactive demo
.\scripts\demo-interactive.ps1
\\\

### Deploy to Azure
\\\ash
azd auth login
azd up
\\\

### Replace localhost URLs
When testing deployed instances, replace \localhost:8080\ with your Container App URL from:
\\\powershell
azd env get-value webHost
\\\

## 📝 Documentation Standards

All guides follow these conventions:
- ✅ Prerequisites listed at the top
- 💻 Code examples in both PowerShell and Bash
- 🎯 Clear testing instructions
- 🔗 Cross-references to related guides
- ⚠️ Migration notices for deprecated features

## 🆕 Recent Updates

- **Dec 2025**: Updated manifest references (archived unused files)
- **Dec 2025**: Added deployment context to all localhost URLs
- **Dec 2025**: Consolidated demo scripts into interactive demo

---

**Need help?** Check the main [README](../../README.md) or the [troubleshooting docs](../DRASI-TROUBLESHOOTING.md).
