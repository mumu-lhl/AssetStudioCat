#!/usr/bin/env python3
"""
AssetStudioCat MCP Server Bridge
A lightweight bridge that connects any MCP-compliant AI assistant (Antigravity, Cursor, Claude Desktop)
to the running AssetStudioCat GUI via its local REST API.

Zero external dependencies: uses Python standard library (sys, json, urllib, os).
"""

import sys
import os
import json
import urllib.request
import urllib.error
import urllib.parse
from typing import Any, Dict, Optional

# Default API URL; can be overridden via ASSETSTUDIO_API_URL or ~/.assetstudio_cat_api.json
DEFAULT_API_URL = "http://127.0.0.1:23333"


def get_base_url() -> str:
    if "ASSETSTUDIO_API_URL" in os.environ:
        return os.environ["ASSETSTUDIO_API_URL"].rstrip("/")

    discovery_path = os.path.expanduser("~/.assetstudio_cat_api.json")
    if os.path.isfile(discovery_path):
        try:
            with open(discovery_path, "r", encoding="utf-8") as f:
                data = json.load(f)
                if "url" in data:
                    return data["url"].rstrip("/")
        except Exception:
            pass

    return DEFAULT_API_URL


def http_get(path: str, params: Optional[Dict[str, Any]] = None) -> Dict[str, Any]:
    base = get_base_url()
    url = f"{base}{path}"
    if params:
        query_string = urllib.parse.urlencode({k: v for k, v in params.items() if v is not None})
        if query_string:
            url += f"?{query_string}"

    req = urllib.request.Request(url, headers={"Accept": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        error_body = e.read().decode("utf-8")
        try:
            err_json = json.loads(error_body)
            msg = err_json.get("error", str(e))
        except Exception:
            msg = error_body or str(e)
        raise RuntimeError(f"GUI API Error ({e.code}): {msg}")
    except urllib.error.URLError as e:
        raise RuntimeError(f"Cannot connect to AssetStudioCat GUI at {base}. Is the GUI running? Error: {e.reason}")


def http_post(path: str, body: Dict[str, Any]) -> Dict[str, Any]:
    base = get_base_url()
    url = f"{base}{path}"
    data = json.dumps(body).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=data,
        headers={"Content-Type": "application/json", "Accept": "application/json"}
    )
    try:
        with urllib.request.urlopen(req, timeout=60) as resp:
            return json.loads(resp.read().decode("utf-8"))
    except urllib.error.HTTPError as e:
        error_body = e.read().decode("utf-8")
        try:
            err_json = json.loads(error_body)
            msg = err_json.get("error", str(e))
        except Exception:
            msg = error_body or str(e)
        raise RuntimeError(f"GUI API Error ({e.code}): {msg}")
    except urllib.error.URLError as e:
        raise RuntimeError(f"Cannot connect to AssetStudioCat GUI at {base}. Is the GUI running? Error: {e.reason}")


# ==============================================================================
# MCP Tool Definitions
# ==============================================================================

TOOLS = [
    {
        "name": "get_status",
        "description": "Get the current status of AssetStudioCat GUI, including loaded game directory, total asset count, and asset counts by type.",
        "inputSchema": {
            "type": "object",
            "properties": {},
            "additionalProperties": False
        }
    },
    {
        "name": "search_assets",
        "description": "Search assets across the loaded Unity project by keyword, type (e.g. 'Shader', 'Material', 'Texture2D', 'Mesh'), and container path. Results are paginated.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Keyword to search in asset name or container path (e.g. 'chara', 'face', 'toon', 'skin')."
                },
                "type": {
                    "type": "string",
                    "description": "Filter by Unity asset type name (e.g. 'Shader', 'Material', 'Texture2D', 'Mesh', 'GameObject', 'TextAsset', 'MonoBehaviour'). Leave empty to search all types."
                },
                "container": {
                    "type": "string",
                    "description": "Optional container directory path filter."
                },
                "limit": {
                    "type": "integer",
                    "description": "Maximum number of items to return (default: 20, max: 100).",
                    "default": 20
                },
                "offset": {
                    "type": "integer",
                    "description": "Paging offset (default: 0).",
                    "default": 0
                }
            },
            "additionalProperties": False
        }
    },
    {
        "name": "inspect_asset",
        "description": "Inspect detailed contents of an asset. For Shaders, returns decompiled source or Properties block; for Materials, returns texture/shader bindings; for Meshes, returns geometry stats. Output is bounded by max_lines to protect context window.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "asset_id": {
                    "type": "integer",
                    "description": "Unique numeric ID of the asset."
                },
                "view_mode": {
                    "type": "string",
                    "enum": ["summary", "raw"],
                    "description": "Inspection mode: 'summary' (default, extracts vital Properties/Tags/texture slots with minimal tokens) or 'raw' (paged full decompiled text).",
                    "default": "summary"
                },
                "start_line": {
                    "type": "integer",
                    "description": "Starting line number for raw text pagination (1-indexed, default: 1).",
                    "default": 1
                },
                "max_lines": {
                    "type": "integer",
                    "description": "Maximum number of lines to return in this call (default: 200, max: 1000).",
                    "default": 200
                },
                "max_bytes": {
                    "type": "integer",
                    "description": "Maximum byte size before truncation (default: 16384, max: 1048576).",
                    "default": 16384
                }
            },
            "required": ["asset_id"],
            "additionalProperties": False
        }
    },
    {
        "name": "get_asset_references",
        "description": "Analyze cross-asset references. For a Material, finds its Shader and referenced Textures; for a Shader, finds Materials that use it; for a GameObject, finds parent/children transforms.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "asset_id": {
                    "type": "integer",
                    "description": "The asset ID to find references for."
                },
                "limit": {
                    "type": "integer",
                    "description": "Max number of references to return (default: 20).",
                    "default": 20
                }
            },
            "required": ["asset_id"],
            "additionalProperties": False
        }
    },
    {
        "name": "export_assets",
        "description": "Export one or more assets to a target directory on disk (e.g. models to FBX, textures to PNG, shaders to .shader, audio to WAV).",
        "inputSchema": {
            "type": "object",
            "properties": {
                "asset_ids": {
                    "type": "array",
                    "items": {"type": "integer"},
                    "description": "List of asset IDs to export."
                },
                "output_directory": {
                    "type": "string",
                    "description": "Target folder path on disk where exported files will be saved."
                },
                "format": {
                    "type": "string",
                    "enum": ["converted", "raw"],
                    "description": "Export format: 'converted' (default: PNG/WAV/FBX/.shader) or 'raw' binary bytes.",
                    "default": "converted"
                }
            },
            "required": ["asset_ids", "output_directory"],
            "additionalProperties": False
        }
    },
    {
        "name": "get_scene_hierarchy",
        "description": "Retrieve or search scene hierarchy nodes (GameObjects / Transforms). Used to locate character model roots (e.g. searching for character name or body root) for complete FBX export.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Optional search keyword to find nodes by name across the scene hierarchy (e.g. 'Character', 'Root', 'Body', 'Armature')."
                },
                "max_depth": {
                    "type": "integer",
                    "description": "Maximum tree depth to return when query is empty (default: 3, max: 20).",
                    "default": 3
                },
                "limit": {
                    "type": "integer",
                    "description": "Maximum number of nodes to return (default: 30, max: 200).",
                    "default": 30
                }
            },
            "additionalProperties": False
        }
    },
    {
        "name": "export_scene_model",
        "description": "Export a character or scene model from the scene hierarchy as a complete rigged FBX file with bones, skinned meshes, and textures, equivalent to the GUI 'Export Selected Model' action.",
        "inputSchema": {
            "type": "object",
            "properties": {
                "transform_id": {
                    "type": "integer",
                    "description": "The unique asset ID of the Transform / RectTransform node (obtained from get_scene_hierarchy or search_assets)."
                },
                "transform_path_id": {
                    "type": "integer",
                    "description": "The Unity PathID of the Transform / RectTransform node (alternative to transform_id)."
                },
                "output_directory": {
                    "type": "string",
                    "description": "Target directory on disk where the FBX model and associated textures will be exported."
                }
            },
            "required": ["output_directory"],
            "additionalProperties": False
        }
    }
]


# ==============================================================================
# Tool Execution Handlers
# ==============================================================================

def execute_tool(name: str, args: Dict[str, Any]) -> str:
    if name == "get_status":
        data = http_get("/api/status")
        return json.dumps(data, indent=2, ensure_ascii=False)

    elif name == "search_assets":
        params = {
            "q": args.get("query"),
            "type": args.get("type"),
            "container": args.get("container"),
            "limit": args.get("limit", 20),
            "offset": args.get("offset", 0)
        }
        data = http_get("/api/assets/search", params)
        return json.dumps(data, indent=2, ensure_ascii=False)

    elif name == "inspect_asset":
        params = {
            "id": args["asset_id"],
            "mode": args.get("view_mode", "summary"),
            "start_line": args.get("start_line", 1),
            "max_lines": args.get("max_lines", 200),
            "max_bytes": args.get("max_bytes", 16384)
        }
        data = http_get("/api/assets/inspect", params)
        return json.dumps(data, indent=2, ensure_ascii=False)

    elif name == "get_asset_references":
        params = {
            "id": args["asset_id"],
            "limit": args.get("limit", 20)
        }
        data = http_get("/api/assets/references", params)
        return json.dumps(data, indent=2, ensure_ascii=False)

    elif name == "export_assets":
        body = {
            "asset_ids": args["asset_ids"],
            "output_directory": args["output_directory"],
            "format": args.get("format", "converted")
        }
        data = http_post("/api/assets/export", body)
        return json.dumps(data, indent=2, ensure_ascii=False)

    elif name == "get_scene_hierarchy":
        params = {
            "q": args.get("query"),
            "max_depth": args.get("max_depth", 3),
            "limit": args.get("limit", 30)
        }
        data = http_get("/api/scene/hierarchy", params)
        return json.dumps(data, indent=2, ensure_ascii=False)

    elif name == "export_scene_model":
        body: Dict[str, Any] = {
            "output_directory": args["output_directory"]
        }
        if "transform_id" in args:
            body["transform_id"] = args["transform_id"]
        if "transform_path_id" in args:
            body["transform_path_id"] = args["transform_path_id"]

        data = http_post("/api/scene/export", body)
        return json.dumps(data, indent=2, ensure_ascii=False)

    else:
        raise ValueError(f"Unknown tool: {name}")


# ==============================================================================
# Standard MCP JSON-RPC Stdio Protocol Loop
# ==============================================================================

def send_response(req_id: Any, result: Any = None, error: Optional[Dict[str, Any]] = None):
    res: Dict[str, Any] = {"jsonrpc": "2.0", "id": req_id}
    if error is not None:
        res["error"] = error
    else:
        res["result"] = result
    line = json.dumps(res) + "\n"
    sys.stdout.write(line)
    sys.stdout.flush()


def run_stdio():
    # Configure UTF-8 on stdio
    if hasattr(sys.stdin, "reconfigure"):
        sys.stdin.reconfigure(encoding="utf-8")
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")

    for raw_line in sys.stdin:
        line = raw_line.strip()
        if not line:
            continue

        try:
            req = json.loads(line)
        except json.JSONDecodeError:
            continue

        req_id = req.get("id")
        method = req.get("method")
        params = req.get("params", {})

        # Handle MCP Methods
        if method == "initialize":
            send_response(req_id, {
                "protocolVersion": "2024-11-05",
                "capabilities": {
                    "tools": {}
                },
                "serverInfo": {
                    "name": "assetstudio-cat-mcp",
                    "version": "1.0.0"
                }
            })

        elif method == "notifications/initialized":
            # Client acknowledgement, no reply needed
            pass

        elif method == "ping":
            send_response(req_id, {})

        elif method == "tools/list":
            send_response(req_id, {
                "tools": TOOLS
            })

        elif method == "tools/call":
            tool_name = params.get("name")
            tool_args = params.get("arguments", {})
            try:
                content_text = execute_tool(tool_name, tool_args)
                send_response(req_id, {
                    "content": [
                        {
                            "type": "text",
                            "text": content_text
                        }
                    ],
                    "isError": False
                })
            except Exception as e:
                send_response(req_id, {
                    "content": [
                        {
                            "type": "text",
                            "text": f"Error executing {tool_name}: {str(e)}"
                        }
                    ],
                    "isError": True
                })

        else:
            if req_id is not None:
                send_response(req_id, error={
                    "code": -32601,
                    "message": f"Method not found: {method}"
                })


if __name__ == "__main__":
    run_stdio()
