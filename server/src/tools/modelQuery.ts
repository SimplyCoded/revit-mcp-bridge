import type { Tool, CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import { postToRevit } from "../client/revitClient.js";

export const tools: Tool[] = [
  {
    name: "get_revit_project_info",
    description:
      "Get basic information about the currently open Revit project: project name, number, client, address, and status.",
    inputSchema: {
      type: "object",
      properties: {},
    },
  },
  {
    name: "say_hello",
    description:
      "Diagnostic tool: shows a Hello World dialog in Revit and returns the Revit version and open project title. Use to verify the MCP bridge is connected.",
    inputSchema: {
      type: "object",
      properties: {},
    },
  },
];

export async function handleCall(
  name: string,
  _args: Record<string, unknown>
): Promise<CallToolResult | undefined> {
  if (name === "get_revit_project_info") return postToRevit("get_project_info", {});
  if (name === "say_hello") return postToRevit("say_hello", {});
  return undefined;
}
