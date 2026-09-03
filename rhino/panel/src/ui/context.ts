import type { PanelCommand } from '../protocol/events.js';
import type { Store } from '../state/store.js';
import type { UiState } from '../state/ui.js';
import type { Zoom } from '../state/zoom.js';

/** Everything a view needs: host state, local view state, and one way out to the host. */
export interface PanelContext {
  readonly store: Store;
  readonly ui: UiState;
  readonly zoom: Zoom;
  /** False when running against the mock in a plain browser. */
  readonly native: boolean;
  send(command: PanelCommand): void;
  copy(text: string): void;
  openLink(url: string): void;
  /** Sends the composer's draft, attachments and picked context as one prompt. */
  submit(text?: string): void;
}
