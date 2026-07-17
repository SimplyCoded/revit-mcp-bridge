import type { Tool, CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import { postToRevit } from "../client/revitClient.js";

export const tools: Tool[] = [
  {
    name: "get_sheets",
    description:
      "List all sheets in the currently open Revit project. " +
      "Returns sheet number, name, element ID, title block ID, and placed viewport count for each sheet.",
    inputSchema: { type: "object", properties: {} },
  },
  {
    name: "get_title_blocks",
    description:
      "List all title block family types loaded in the Revit project. " +
      "Use the returned IDs when calling create_sheet or duplicate_sheet.",
    inputSchema: { type: "object", properties: {} },
  },
  {
    name: "create_sheet",
    description:
      "Create a new empty sheet in the Revit project. " +
      "Use get_title_blocks to find available title block type IDs.",
    inputSchema: {
      type: "object",
      required: ["sheetNumber", "sheetName"],
      properties: {
        sheetNumber: {
          type: "string",
          description: "Sheet number, e.g. 'A-101'. Must be unique in the project.",
        },
        sheetName: {
          type: "string",
          description: "Sheet name, e.g. 'Floor Plan - Level 1'.",
        },
        titleBlockTypeId: {
          type: "integer",
          description:
            "Element ID of the title block family type to use. " +
            "Omit to create a sheet without a title block.",
        },
      },
    },
  },
  {
    name: "duplicate_sheet",
    description:
      "Duplicate an existing sheet: creates a new sheet with the same title block, " +
      "then duplicates each placed view onto the new sheet. " +
      "Note: each view is independently duplicated (Revit does not allow the same view on two sheets). " +
      "Views that cannot be duplicated (e.g. schedules, legends) are skipped and reported.",
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
          description: "Sheet number for the new sheet. Must be unique in the project.",
        },
        newSheetName: {
          type: "string",
          description: "Name for the new sheet.",
        },
      },
    },
  },
];

export async function handleCall(
  name: string,
  args: Record<string, unknown>
): Promise<CallToolResult | undefined> {
  if (name === "get_sheets") return postToRevit("get_sheets", {});
  if (name === "get_title_blocks") return postToRevit("get_title_blocks", {});

  if (name === "create_sheet") {
    const { sheetNumber, sheetName, titleBlockTypeId } = args as {
      sheetNumber: string;
      sheetName: string;
      titleBlockTypeId?: number;
    };
    return postToRevit("create_sheet", {
      sheetNumber,
      sheetName,
      ...(titleBlockTypeId !== undefined ? { titleBlockTypeId } : {}),
    });
  }

  if (name === "duplicate_sheet") {
    const { sourceSheetId, newSheetNumber, newSheetName } = args as {
      sourceSheetId: number;
      newSheetNumber: string;
      newSheetName: string;
    };
    return postToRevit("duplicate_sheet", { sourceSheetId, newSheetNumber, newSheetName });
  }

  return undefined;
}
