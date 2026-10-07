import type { Tool, CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import { postToRevit } from "../client/revitClient.js";

export const tools: Tool[] = [
  {
    name: "get_sheets",
    description:
      "Get all sheets in the Revit project. Returns sheet number, name, ID, title block ID, and viewport count for each sheet.",
    inputSchema: {
      type: "object",
      properties: {},
    },
  },
  {
    name: "get_title_blocks",
    description:
      "Get all title block family types loaded in the project. Returns family name, type name, and element ID. Use the IDs with create_sheet.",
    inputSchema: {
      type: "object",
      properties: {},
    },
  },
  {
    name: "create_sheet",
    description:
      "Create a new sheet in the Revit project. Use get_title_blocks to find valid titleBlockTypeId values.",
    inputSchema: {
      type: "object",
      required: ["sheetNumber", "sheetName"],
      properties: {
        sheetNumber: {
          type: "string",
          description: "Sheet number (e.g. 'A-101', 'S-001'). Must be unique in the project.",
        },
        sheetName: {
          type: "string",
          description: "Sheet name / title (e.g. 'Ground Floor Plan').",
        },
        titleBlockTypeId: {
          type: "integer",
          description:
            "Element ID of the title block family type to use. " +
            "Use get_title_blocks to find valid IDs. If omitted, no title block is placed.",
        },
      },
    },
  },
  {
    name: "duplicate_sheet",
    description:
      "Duplicate an existing sheet including all its viewports. " +
      "Each duplicatable view is copied as an independent view on the new sheet.",
    inputSchema: {
      type: "object",
      required: ["sourceSheetId", "newSheetNumber", "newSheetName"],
      properties: {
        sourceSheetId: {
          type: "integer",
          description: "Element ID of the sheet to duplicate. Use get_sheets to find IDs.",
        },
        newSheetNumber: {
          type: "string",
          description: "Sheet number for the new duplicate sheet (must be unique).",
        },
        newSheetName: {
          type: "string",
          description: "Sheet name for the new duplicate sheet.",
        },
      },
    },
  },
];

export async function handleCall(
  name: string,
  args: Record<string, unknown>
): Promise<CallToolResult | undefined> {
  if (name === "get_sheets")       return postToRevit("get_sheets", {});
  if (name === "get_title_blocks") return postToRevit("get_title_blocks", {});
  if (name === "create_sheet")     return postToRevit("create_sheet", args);
  if (name === "duplicate_sheet")  return postToRevit("duplicate_sheet", args);
  return undefined;
}
