# AssetStudioCat MCP Server Bridge

This directory contains the lightweight Model Context Protocol (MCP) server bridge for **AssetStudioCat**. It connects your AI coding assistant (Antigravity, Claude Desktop, Cursor, Windsurf, etc.) directly to the running AssetStudioCat GUI over a local HTTP connection.

## Features

- **General-purpose asset discovery**: Search any asset type (`Shader`, `Material`, `Texture2D`, `Mesh`, `GameObject`, etc.) with `search_assets`.
- **Protected context window**:
  - `inspect_asset` defaults to `view_mode="summary"`, extracting only vital `Properties { ... }` blocks and SubShader tags for Shaders.
  - Full source mode (`view_mode="raw"`) provides line-based pagination (`start_line`, `max_lines`) to inspect large shaders safely without blowing out context tokens.
- **Cross-referencing (`get_asset_references`)**: Find which materials use a shader, or which textures a material binds to.
- **Direct export (`export_assets`)**: Export discovered character models (FBX), shaders (.shader), or textures (PNG) directly to your local folder.
- **Zero external dependencies**: Implemented using pure Python 3 standard library (no pip packages needed).

---

## Configuration

### 1. In Google Antigravity / Gemini CLI

Add to your `mcp.json` (or pass via command line):

```json
{
  "mcpServers": {
    "assetstudio-cat": {
      "command": "python3",
      "args": [
        "/path/to/AssetStudioCat/mcp/assetstudio_mcp.py"
      ]
    }
  }
}
```

### 2. In Claude Desktop

Add to `~/.config/Claude/claude_desktop_config.json` (Linux) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

```json
{
  "mcpServers": {
    "assetstudio-cat": {
      "command": "python3",
      "args": [
        "/path/to/AssetStudioCat/mcp/assetstudio_mcp.py"
      ]
    }
  }
}
```

### 3. In Cursor / Windsurf

Add a new stdio MCP server:
- **Command**: `python3`
- **Args**: `/path/to/AssetStudioCat/mcp/assetstudio_mcp.py`

---

## How it Works

1. Start **AssetStudioCat** GUI:
   ```bash
   dotnet run --project AssetStudioGUI.Avalonia
   ```
   The GUI automatically starts a local REST endpoint on `http://127.0.0.1:23333` (or writes the active port to `~/.assetstudio_cat_api.json`).
2. Open your game data folder or asset bundles in the GUI.
3. In your AI chat window, prompt the AI:
   > *"Find all toon / NPR character shaders in the loaded project, inspect their shadow and outline properties, and export them along with the main character model to `/tmp/miku_export`."*
4. The AI will autonomously call `search_assets`, `inspect_asset`, `get_asset_references`, and `export_assets` to complete the workflow.
