import axios from "axios";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import type { CallToolResult } from "@modelcontextprotocol/sdk/types.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

interface Config {
  host?: string;
  port?: number;
  authToken: string;
}

function loadConfig(): Config {
  const configPath = path.resolve(__dirname, "../../../config.json");
  if (!fs.existsSync(configPath)) {
    throw new Error(
      `config.json not found at ${configPath}. ` +
        "Copy config.example.json to config.json and set a strong authToken."
    );
  }
  const raw = JSON.parse(fs.readFileSync(configPath, "utf-8")) as Config;
  if (!raw.authToken || raw.authToken === "change-me-to-a-strong-random-secret") {
    throw new Error(
      "config.json authToken is missing or still the example value. " +
        "Set a strong unique secret before starting the server."
    );
  }
  return raw;
}

let _config: Config | null = null;

function getConfig(): Config {
  if (!_config) _config = loadConfig();
  return _config;
}

export async function postToRevit(
  command: string,
  args: Record<string, unknown> = {},
  timeoutMs = 10000
): Promise<CallToolResult> {
  const cfg = getConfig();
  const host = cfg.host ?? "127.0.0.1";
  const port = cfg.port ?? 8080;
  const url = `http://${host}:${port}/revit/`;

  try {
    const { data } = await axios.post(
      url,
      { command, args },
      {
        timeout: timeoutMs,
        headers: {
          "Content-Type": "application/json",
          "X-Revit-MCP-Token": cfg.authToken,
        },
      }
    );
    return {
      content: [{ type: "text", text: JSON.stringify(data) }],
    };
  } catch (err: unknown) {
    const msg =
      err instanceof Error
        ? err.message
        : "Unknown error communicating with Revit plugin";
    return {
      isError: true,
      content: [{ type: "text", text: `Revit bridge error: ${msg}` }],
    };
  }
}
