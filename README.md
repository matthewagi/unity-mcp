<p align="center">
  <img src="https://img.shields.io/badge/Unity-2021.3%2B-black?logo=unity" alt="Unity 2021.3+"/>
  <img src="https://img.shields.io/badge/License-MIT-green" alt="MIT License"/>
  <img src="https://img.shields.io/badge/MCP-Compatible-blue" alt="MCP Compatible"/>
  <img src="https://img.shields.io/badge/C%23_Lines-5.7k-blueviolet" alt="5.7k Lines of C#"/>
  <img src="https://img.shields.io/badge/CPU_When_Idle-0%25-brightgreen" alt="0% CPU When Idle"/>
</p>

<h1 align="center">Unity x Claude</h1>

<p align="center">
  <strong>Control the Unity Editor with natural language.</strong><br/>
  An MCP server that gives Claude 20 tools to create scripts, build scenes, modify components, manage assets, change settings, run builds, and execute C# — all from a conversation.
</p>

---

## Features

**20 tools** covering every part of the Unity Editor:

- **Scene control** — create, delete, duplicate, and inspect GameObjects. Read the full hierarchy with depth control, filter by tag or component.
- **Component system** — add, remove, read, and modify any component. Batch-update multiple properties in a single call. Wire object references between components.
- **Script generation** — create MonoBehaviours, ScriptableObjects, EditorWindows from templates. Modify existing scripts with find/replace, method injection, or full rewrites.
- **Asset management** — search by name or type, create Materials, Prefabs, ScriptableObjects, Folders. Import and re-import assets.
- **Project settings** — read and write Physics, Player, Quality, Audio, Time, Graphics, Input, Tags, Editor, and Navigation settings. All changes undoable.
- **Build system** — build for Windows, macOS, Linux, WebGL, Android, or iOS. Development builds, custom BuildOptions.
- **Editor commands** — play, pause, stop, step, save, undo, redo, compile, refresh. Run any Unity menu item.
- **Live C# execution** — run arbitrary C# inside the editor with full access to UnityEngine and UnityEditor APIs. Compiled in-memory — no temp files, no domain reload, instant results.

**Built for performance:**

- **Zero CPU when idle** — single background thread blocked on `Socket.Poll`. No async, no ThreadPool, no polling loops, no timers.
- **Survives domain reloads** — auto-restarts after script recompilation via `[InitializeOnLoad]`. 5-second safety net ensures the server is always running.
- **Pure C#** — no external dependencies, no DLLs, no NuGet packages. Just drop the folder into your project.

**Built for safety:**

- **Everything is undoable** — all destructive operations go through Unity's Undo system. Ctrl+Z works on every change Claude makes.
- **Dangerous operations blocked** — `execute_csharp` blocks `Process.Start`, `Environment.Exit`, `Registry.*`, and other unsafe calls.
- **Port cleanup** — server registers `ProcessExit` and `DomainUnload` handlers so the port is always released.

**Built for testing:**

- **Runtime data during Play mode** — component queries automatically include live position, rotation, velocity, and sleep state for physics objects.
- **Screenshot verification** — capture what the camera sees, describe visible objects with screen positions, simulate clicks on objects.
- **Smart snapshots** — schedule timed screenshots at animation keyframes to verify visual behavior with minimum captures.

**Works with:**

- Claude Desktop (via stdio bridge)
- Claude Code (direct HTTP)
- Cursor (direct HTTP)
- Any MCP-compatible client

---

## Installation

### Step 1 — Add to your Unity project

Clone this repo (or download ZIP) and copy everything into your project's `Packages` folder:

```
YourProject/
  Packages/
    com.claude.unity-mcp/       ← put it here
      Editor/
      package.json
      mcp-bridge.mjs
```

Open Unity. The console should show:

```
[MCP] Ready on port 9999
```

> Verify at **Window > Claude MCP** — shows server status, port, and all available tools.

### Step 2 — Connect Claude

Open your Claude config file:

| OS | Path |
|---|---|
| macOS | `~/Library/Application Support/Claude/claude_desktop_config.json` |
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |

Add this (create the file if it doesn't exist):

```json
{
  "mcpServers": {
    "unity": {
      "command": "node",
      "args": [
        "/full/path/to/YourProject/Packages/com.claude.unity-mcp/mcp-bridge.mjs"
      ]
    }
  }
}
```

> **Shortcut:** In Unity, click **Window > Claude MCP > Copy Config** — it copies the JSON with the correct path. Just paste it into the file.

<details>
<summary>Using Claude Code or Cursor instead?</summary>

These support direct HTTP — no bridge needed:

```json
{
  "mcpServers": {
    "unity": {
      "url": "http://localhost:9999/mcp"
    }
  }
}
```
</details>

### Step 3 — Add the skill files

This is what makes Claude actually good at using the tools. Without these files, Claude can connect but won't know property paths, workarounds, batch patterns, or testing workflows.

Copy `SKILL.md` and `WORKFLOW.md` into Claude's skills folder:

```bash
# macOS / Linux
mkdir -p ~/.claude/skills/unity-x-claude
cp SKILL.md WORKFLOW.md ~/.claude/skills/unity-x-claude/
```

```powershell
# Windows (PowerShell)
mkdir -Force "$env:USERPROFILE\.claude\skills\unity-x-claude"
copy SKILL.md, WORKFLOW.md "$env:USERPROFILE\.claude\skills\unity-x-claude\"
```

<details>
<summary>Using Claude Code or Cursor?</summary>

Put them in your project's `.claude/skills/` folder instead:

```bash
mkdir -p YourProject/.claude/skills/unity-x-claude
cp SKILL.md WORKFLOW.md YourProject/.claude/skills/unity-x-claude/
```
</details>

**What these files teach Claude:**

| File | What Claude learns |
|------|-------------------|
| `SKILL.md` | All 20 tools with usage patterns, property paths for every common component (Transform, Rigidbody, Collider, Camera, Light, AudioSource), batch operation patterns, depth strategies, settings categories |
| `WORKFLOW.md` | Architecture, known gotchas with workarounds, runtime data inspection, screenshot testing pipeline, domain reload handling, port conflict recovery, safe C# execution patterns |

### Step 4 — Restart and test

Restart Claude Desktop (or start a new chat). Try:

- *"What's in my Unity scene?"*
- *"Create a red cube at position 0, 3, 0 with a Rigidbody"*
- *"Change the gravity to -20 and set the quality level to Ultra"*

If Claude responds with your scene data, you're done.

---

## Tool Reference

| Category | Tools | Examples |
|----------|-------|---------|
| **Scene** | `get_scene`, `create_gameobject`, `delete`, `duplicate` | Read hierarchy, spawn objects with components, clone objects |
| **Components** | `get_components`, `set_property`, `add_component`, `remove_component` | Inspect properties, batch-update 10+ values, wire references |
| **Scripts** | `create_script`, `modify_script` | Generate MonoBehaviours, find/replace in code, add methods |
| **Assets** | `search_assets`, `get_asset`, `create_asset`, `import_asset` | Find materials, create prefabs, re-import textures |
| **Editor** | `editor_command`, `get_selection`, `set_selection`, `scene_view` | Play/stop, save, undo, frame camera on object |
| **Settings** | `get_settings`, `set_settings` | Read/write Physics, Player, Quality, Audio, Time, Graphics |
| **Build** | `build`, `manage_packages`, `get_console` | Build for any platform, add packages, read errors |
| **Code** | `execute_csharp` | Run any C# with full Unity API access, in-memory compilation |

> All tools are prefixed with `unity_` (e.g., `unity_get_scene`). See [SKILL.md](SKILL.md) for the full reference with property paths and workflow patterns.

---

## Architecture

```
Claude Desktop / Claude Code / Cursor
        |
        | stdio (JSON-RPC 2.0)
        v
  mcp-bridge.mjs             Node.js — translates stdio ↔ HTTP
        |
        | HTTP POST localhost:9999/mcp
        v
  Unity Editor (MCPServer)
        |
        ├── StreamableHttpServer    Raw TcpListener, single thread, Socket.Poll
        ├── MainThreadDispatcher    ConcurrentQueue → EditorApplication.update
        ├── JsonRpcHandler          JSON-RPC 2.0 parsing
        ├── 8 Tool modules          Scene, Component, Asset, Script, Editor, Settings, Build, Execute
        └── Serialization           GameObject → JSON, Asset → JSON, Property helpers
```

### Why zero CPU when idle

| Component | Idle behavior |
|-----------|--------------|
| `StreamableHttpServer` | Thread blocked on `Socket.Poll(1s)` — true kernel wait, zero CPU |
| `MainThreadDispatcher` | `ConcurrentQueue.TryDequeue` returns false — single boolean check per frame |
| `MCPServer.Tick()` | One `EditorApplication.update` callback — nanosecond cost when empty |
| Status Window | No continuous repaint — updates only on interaction |

---

## Compatibility

| | Supported |
|---|---|
| **Unity** | 2021.3 LTS and newer (tested on Unity 6.0) |
| **Render Pipeline** | URP, HDRP, Built-in |
| **OS** | macOS, Windows |
| **MCP Clients** | Claude Desktop, Claude Code, Cursor, or any MCP-compatible client |
| **Node.js** | Required for stdio bridge (Claude Desktop). Not needed for direct HTTP clients. |

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Claude can't connect | Make sure Unity is open and **Window > Claude MCP** shows "Running" |
| Server not starting | Enable the toggle in **Window > Claude MCP**, or recompile (Ctrl+R) |
| Wrong project path | Use **Window > Claude MCP > Copy Config** to get the correct path |
| Node.js not found | Install Node.js — the bridge needs it: `node --version` |
| Port 9999 in use | Change port in **Window > Claude MCP > Advanced Settings**, update config to match |
| Calls fail after recompile | Normal — server restarts in 2-5 seconds. Just retry. |

---

## Contributing

Pull requests welcome. If you add a new tool, follow the pattern in `Editor/Tools/` and update `SKILL.md` with usage examples.

## License

[MIT](LICENSE)
