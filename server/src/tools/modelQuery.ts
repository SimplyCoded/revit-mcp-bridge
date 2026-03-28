import type { Tool, CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import { postToRevit } from "../client/revitClient.js";

export const tools: Tool[] = [
  {
    name: "get_revit_project_info",
    description: "Get basic information about the currently open Revit project (e.g., project name).",
    inputSchema: {
      type: "object",
      properties: {},
    },
  },
  {
    name: "say_hello",
    description:
      "Diagnostic tool — opens a TaskDialog in Revit and returns the Revit version " +
      "and project name. Use this to verify the MCP bridge is alive end-to-end.",
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
  if (name === "get_revit_project_info") {
    return postToRevit("get_project_info", {});
  }
  if (name === "say_hello") {
    return postToRevit("say_hello", {});
  }
  return undefined;
}
