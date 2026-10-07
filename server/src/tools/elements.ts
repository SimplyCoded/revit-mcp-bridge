import type { Tool, CallToolResult } from "@modelcontextprotocol/sdk/types.js";
import { postToRevit } from "../client/revitClient.js";

const POINT_SCHEMA = {
  type: "object",
  required: ["x", "y"],
  properties: {
    x: { type: "number", description: "X coordinate in mm." },
    y: { type: "number", description: "Y coordinate in mm." },
    z: { type: "number", description: "Z coordinate in mm (elevation). Defaults to 0." },
  },
} as const;

export const tools: Tool[] = [
  // ── Phase 1 port ──────────────────────────────────────────────────────────
  // (none — all Phase 1 tools live in modelInfo.ts)

  // ── Phase 2 — creation ────────────────────────────────────────────────────
  {
    name: "create_level",
    description: "Create a new level in the Revit project at a given elevation.",
    inputSchema: {
      type: "object",
      required: ["elevationMm"],
      properties: {
        elevationMm: { type: "number", description: "Elevation in millimetres above project base." },
        name:        { type: "string", description: "Optional level name (e.g. 'Level 3'). Auto-assigned if omitted." },
      },
    },
  },
  {
    name: "create_grid",
    description: "Create a grid line between two points in plan.",
    inputSchema: {
      type: "object",
      required: ["startPoint", "endPoint"],
      properties: {
        startPoint: POINT_SCHEMA,
        endPoint:   POINT_SCHEMA,
        name:       { type: "string", description: "Grid name (e.g. 'A', '1'). Auto-assigned if omitted." },
      },
    },
  },
  {
    name: "create_line_based_element",
    description:
      "Create a line-based element (wall, structural beam, pipe, duct, etc.) between two points. " +
      "Use get_available_family_types to find valid familyTypeId values.",
    inputSchema: {
      type: "object",
      required: ["familyTypeId", "startPoint", "endPoint"],
      properties: {
        familyTypeId: { type: "integer", description: "Element ID of the family type to place." },
        startPoint:   POINT_SCHEMA,
        endPoint:     POINT_SCHEMA,
        levelId:      { type: "integer", description: "Element ID of the host level. Nearest level is used if omitted." },
        heightMm:     { type: "number",  description: "Wall height in mm (walls only, default 3000)." },
      },
    },
  },
  {
    name: "create_point_based_element",
    description:
      "Create a point-based element (column, door, window, furniture, equipment, etc.) at a given point. " +
      "Use get_available_family_types to find valid familyTypeId values.",
    inputSchema: {
      type: "object",
      required: ["familyTypeId", "point"],
      properties: {
        familyTypeId: { type: "integer", description: "Element ID of the family type to place." },
        point:        POINT_SCHEMA,
        levelId:      { type: "integer", description: "Host level ID. Nearest level is used if omitted." },
        rotation:     { type: "number",  description: "Rotation in degrees around vertical axis (default 0)." },
      },
    },
  },
  {
    name: "create_room",
    description: "Create a room at a point on a given level.",
    inputSchema: {
      type: "object",
      required: ["levelId", "point"],
      properties: {
        levelId: { type: "integer", description: "Element ID of the level to place the room on." },
        point:   POINT_SCHEMA,
        name:    { type: "string", description: "Room name." },
        number:  { type: "string", description: "Room number." },
      },
    },
  },
  {
    name: "create_surface_based_element",
    description:
      "Create a surface-based element (floor, ceiling, roof) on a level with a polygon boundary. " +
      "Use get_available_family_types with category 'Floors' or 'Ceilings' to find familyTypeId values.",
    inputSchema: {
      type: "object",
      required: ["familyTypeId", "levelId", "boundary"],
      properties: {
        familyTypeId: { type: "integer", description: "Element ID of the floor/ceiling/roof type." },
        levelId:      { type: "integer", description: "Host level ID." },
        boundary: {
          type: "array",
          minItems: 3,
          description: "Array of {x, y, z} points (mm) forming a closed polygon.",
          items: POINT_SCHEMA,
        },
      },
    },
  },
  {
    name: "create_structural_framing_system",
    description:
      "Create a rectangular grid of structural beams between two corner points on a level.",
    inputSchema: {
      type: "object",
      required: ["beamFamilyTypeId", "levelId", "gridStart", "gridEnd"],
      properties: {
        beamFamilyTypeId: { type: "integer", description: "Element ID of the beam family type." },
        levelId:          { type: "integer", description: "Level to place beams on." },
        gridStart:        POINT_SCHEMA,
        gridEnd:          POINT_SCHEMA,
        spacingXMm: { type: "number", description: "Beam spacing in X direction (mm, default 5000)." },
        spacingYMm: { type: "number", description: "Beam spacing in Y direction (mm, default 5000)." },
      },
    },
  },
  // ── Phase 2 — modification ────────────────────────────────────────────────
  {
    name: "delete_element",
    description: "Delete one or more elements by their element IDs.",
    inputSchema: {
      type: "object",
      required: ["elementIds"],
      properties: {
        elementIds: {
          type: "array",
          items: { type: "integer" },
          description: "Array of element IDs to delete.",
        },
      },
    },
  },
  {
    name: "operate_element",
    description: "Move, copy, or rotate an element. Use get_selected_elements or get_current_view_elements to find element IDs.",
    inputSchema: {
      type: "object",
      required: ["elementId", "operation"],
      properties: {
        elementId: { type: "integer", description: "Element ID to operate on." },
        operation: {
          type: "string",
          enum: ["move", "copy", "rotate"],
          description: "Operation to perform.",
        },
        translation: {
          ...POINT_SCHEMA,
          description: "Translation vector in mm. Required for move and copy.",
        } as Record<string, unknown>,
        angleDeg: {
          type: "number",
          description: "Rotation angle in degrees. Required for rotate.",
        },
        axisPoint: {
          ...POINT_SCHEMA,
          description: "Point on the rotation axis (mm). Required for rotate.",
        } as Record<string, unknown>,
        axisDirection: {
          ...POINT_SCHEMA,
          description: "Rotation axis direction vector. Defaults to vertical (Z).",
        } as Record<string, unknown>,
      },
    },
  },
  {
    name: "color_elements",
    description:
      "Color elements in the active view by a parameter value — each unique value gets a distinct color. " +
      "Great for visualising structural material, fire rating, cost code, etc.",
    inputSchema: {
      type: "object",
      required: ["category", "paramName"],
      properties: {
        category:  { type: "string",  description: "Category to color, e.g. 'Walls'." },
        paramName: { type: "string",  description: "Parameter name to group by (exact Revit name)." },
        viewId:    { type: "integer", description: "View to apply colors in. Defaults to active view." },
      },
    },
  },
  {
    name: "create_dimensions",
    description:
      "Create a linear dimension spanning two or more elements in the active view. " +
      "Finds perpendicular faces automatically.",
    inputSchema: {
      type: "object",
      required: ["elementIds"],
      properties: {
        elementIds: {
          type: "array",
          items: { type: "integer" },
          minItems: 2,
          description: "Element IDs to dimension between (in order).",
        },
        direction: {
          ...POINT_SCHEMA,
          description: "Direction of the dimension (perpendicular to the dimensioned faces). Defaults to X axis.",
        } as Record<string, unknown>,
      },
    },
  },
  {
    name: "tag_all_walls",
    description: "Place tags on all walls visible in the active (or specified) view.",
    inputSchema: {
      type: "object",
      properties: {
        viewId: { type: "integer", description: "View to tag in. Defaults to active view." },
      },
    },
  },
  {
    name: "tag_all_rooms",
    description: "Place tags on all rooms visible in the active (or specified) view.",
    inputSchema: {
      type: "object",
      properties: {
        viewId: { type: "integer", description: "View to tag in. Defaults to active view." },
      },
    },
  },
];

export async function handleCall(
  name: string,
  args: Record<string, unknown>
): Promise<CallToolResult | undefined> {
  const p = (extra?: Record<string, unknown>, timeout?: number) =>
    postToRevit(name, { ...args, ...extra }, timeout);

  switch (name) {
    case "create_level":                    return p();
    case "create_grid":                     return p();
    case "create_line_based_element":       return p();
    case "create_point_based_element":      return p();
    case "create_room":                     return p();
    case "create_surface_based_element":    return p();
    case "create_structural_framing_system":return p();
    case "delete_element":                  return p();
    case "operate_element":                 return p();
    case "color_elements":                  return p();
    case "create_dimensions":               return p();
    case "tag_all_walls":                   return p();
    case "tag_all_rooms":                   return p();
    default: return undefined;
  }
}
