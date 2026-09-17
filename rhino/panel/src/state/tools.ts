// The panel's only knowledge of individual tools: which family a name belongs to, for the icon and
// accent on its card. The readable phrase ("placed Circle") is authored host-side, where the real
// args live, so the giant per-tool switch the Eto panel carried does not exist here.

import type { IconName } from '../ui/icons.js';
import type { ToolStatus } from '../protocol/events.js';

export type ToolFamily = 'script' | 'document' | 'geometry' | 'view' | 'grasshopper' | 'question' | 'other';

const PREFIXES: readonly (readonly [string, ToolFamily])[] = [
  ['g1_', 'grasshopper'],
  ['g2_', 'grasshopper'],
  ['run_', 'script'],
  ['script_editor_', 'script'],
  ['get_viewport', 'view'],
  ['set_camera', 'view'],
  ['zoom_', 'view'],
  ['ask_user', 'question'],
];

const EXACT: Readonly<Record<string, ToolFamily>> = {
  open_doc: 'document',
  save_doc: 'document',
  close_doc: 'document',
  list_objects: 'geometry',
  get_selection: 'geometry',
  set_selection: 'geometry',
  set_layer_material: 'geometry',
  get_commands: 'document',
};

export function familyOf(name: string): ToolFamily {
  const exact = EXACT[name];
  if (exact) return exact;
  for (const [prefix, family] of PREFIXES) if (name.startsWith(prefix)) return family;
  return 'other';
}

const ICONS: Readonly<Record<ToolFamily, IconName>> = {
  script: 'terminal',
  document: 'document',
  geometry: 'cube',
  view: 'camera',
  grasshopper: 'graph',
  question: 'question',
  other: 'tool',
};

export function iconFor(family: ToolFamily): IconName {
  return ICONS[family];
}

// A handful of tools earn their own mark because the family icon hides the one thing the card does
// not otherwise say: "opened Grasshopper" is the title for both g1_ and g2_, and "ran python" and
// "ran C#" share the terminal glyph. Everything else stays on its family icon.
const TOOL_ICONS: Readonly<Record<string, IconName>> = {
  run_python: 'python',
  run_csharp: 'csharp',
};

const TOOL_ICON_PREFIXES: readonly (readonly [string, IconName])[] = [
  ['g1_', 'gh1'],
  ['g2_', 'gh2'],
];

export function iconForTool(name: string): IconName {
  const exact = TOOL_ICONS[name];
  if (exact) return exact;
  for (const [prefix, mark] of TOOL_ICON_PREFIXES) if (name.startsWith(prefix)) return mark;
  return iconFor(familyOf(name));
}

export function statusLabel(status: ToolStatus): string {
  switch (status) {
    case 'running':
      return 'running';
    case 'ok':
      return 'done';
    case 'failed':
      return 'failed';
    case 'denied':
      return 'denied';
    case 'unknown':
      return 'no result';
  }
}
