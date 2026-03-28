import axios from "axios";
import { readFileSync } from "fs";
import { resolve } from "path";
import type { CallToolResult } from "@modelcontextprotocol/sdk/types.js";

interface RevitConfig {
  revitPluginUrl?: string;
  authToken?: string;
}

function loadConfig(): RevitConfig {
  try {
    const configPath = resolve(__dirname, "../../../config.json");
    return JSON.parse(readFileSync(configPath, "utf-8"));
  } catch {
    return {};
  }
}

const config = loadConfig();
const REVIT_PLUGIN_URL = config.revitPluginUrl ?? "http://127.0.0.1:8080/revit/";
const AUTH_TOKEN = config.authToken ?? "revit-mcp-secret-2025";

export async function postToRevit(
  command: string,
  args: Record<string, unknown>,
  timeoutMs = 15000
): Promise<CallToolResult> {
  try {
    const response = await axios.post(
      REVIT_PLUGIN_URL,
      { command, args },
      {
        headers: {
          "X-Revit-MCP-Token": AUTH_TOKEN,
          "Content-Type": "application/json",
        },
        timeout: timeoutMs,
      }
    );
    return {
      content: [{ type: "text" as const, text: JSON.stringify(response.data, null, 2) }],
    };
  } catch (error: unknown) {
    const msg = error instanceof Error ? error.message : String(error);
    return {
      content: [{ type: "text" as const, text: `Error communicating with Revit: ${msg}` }],
      isError: true,
    };
  }
}
