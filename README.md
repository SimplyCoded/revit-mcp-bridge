# Revit MCP Bridge

Connects an AI assistant (Claude, Gemini CLI) to Autodesk Revit via the Model Context Protocol. Lets you query and validate your Revit structural model directly from a chat interface.

```
MCP Client (Claude / Gemini) <--stdio--> mcp-server (Node.js) <--HTTP--> revit-plugin (C# inside Revit)
```

## Features

- **Model info** — get the active project name
- **Parameter validation** — health report of missing or empty parameters across structural elements
- **Rule-based auto-fill** — derive and populate parameter values based on model logic (material summary, wall function flags, conditional rules)

Supports: Structural Framing, Structural Columns, Structural Foundations, Walls, Floors.

## Requirements

- Autodesk Revit 2025
- Node.js (in WSL or Windows)
- .NET SDK (for building the plugin)
- WSL2 with mirrored networking (if running the MCP server in WSL)

## Setup

### 1. Config file

Copy the example config and set your auth token:

```bash
cp config.example.json config.json
```

```json
{
  "authToken": "your-secret-token",
  "revitPluginUrl": "http://127.0.0.1:8080/revit/"
}
```

`config.json` is git-ignored and must be present on both the MCP server machine and alongside the installed Revit plugin DLL.

### 2. MCP Server

```bash
cd mcp-server
npm install
npm run build
```

### 3. Revit Plugin

Build from Windows:

```bash
dotnet.exe build "...\revit-plugin\RevitMcpPlugin.csproj" -c Release -r win-x64
```

Install by copying these files to `%APPDATA%\Autodesk\Revit\Addins\2025\`:
- `revit-plugin/bin/Release/win-x64/RevitMcpPlugin.dll`
- `revit-plugin/RevitMcpPlugin.addin` (update the `<Assembly>` path inside it)
- `config.json`

### 4. WSL Networking (if running MCP server in WSL)

Add to `C:\Users\<you>\.wslconfig`:

```ini
[wsl2]
networkingMode=mirrored
```

Then run `wsl --shutdown` from PowerShell and reopen WSL. This allows WSL to reach Revit's `127.0.0.1:8080` listener.

### 5. MCP Client Config

**Gemini CLI** (`C:\Users\<you>\Gemini\.gemini\settings.json`):

```json
{
  "mcpServers": {
    "revit-bridge": {
      "command": "node",
      "args": ["/path/to/revit-mcp-bridge/mcp-server/dist/index.js"]
    }
  }
}
```

**Claude Desktop** (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "revit-bridge": {
      "command": "node",
      "args": ["/path/to/revit-mcp-bridge/mcp-server/dist/index.js"]
    }
  }
}
```

## Available Tools

### `get_revit_project_info`
Returns the name of the currently open Revit project.

### `validate_parameters`
Checks whether required parameters are set on structural elements.

```json
{
  "category": "OST_StructuralFraming",
  "additional_params": ["SBB_AssetID", "SBB_MaintenanceZone"]
}
```

Returns a health report with total, passed, failed counts and per-element details of missing or empty parameters.

### `apply_parameter_rules`
Derives and fills parameter values based on model conditions. Use `"action": "suggest"` to preview changes or `"action": "auto-fill"` to write them.

```json
{
  "category": "OST_Walls",
  "action": "suggest",
  "rules": [
    { "type": "material_summary", "target_param": "Material_Summary" },
    { "type": "wall_function_flag", "target_param": "IsExterior", "exterior_value": "True", "interior_value": "False" },
    { "type": "if_then", "if_param": "FUNCTION_PARAM", "if_value": "Exterior", "set_param": "SBB_IsExterior", "set_value": "True" }
  ]
}
```

**Rule types:**
| Type | Description |
|------|-------------|
| `material_summary` | Aggregates material names from the element into a target parameter |
| `wall_function_flag` | Sets a parameter based on whether the wall type is Interior or Exterior |
| `if_then` | Conditionally sets a parameter when another parameter matches a value |
