// Purely local view state: nothing here needs to survive a reload or reach the host.

import { computed, signal, type ReadSignal } from '../core/signal.js';
import type { Attachment, ContextItem } from '../protocol/events.js';

export type Overlay = 'none' | 'history' | 'agents' | 'context' | 'mcps';

// History fills the panel, so it is a page being read rather than a menu to dismiss.
const IS_MENU: Record<Overlay, boolean> = {
  none: false,
  history: false,
  agents: true,
  context: true,
  mcps: true,
};

export class UiState {
  readonly draft = signal('');
  readonly attachments = signal<readonly Attachment[]>([]);
  readonly pickedContext = signal<readonly ContextItem[]>([]);
  readonly overlay = signal<Overlay>('none');
  readonly historyQuery = signal('');
  readonly expandedTools = signal<ReadonlySet<string>>(new Set<string>());
  /** False once the user scrolls away from the tail; new output then shows a jump affordance. */
  readonly pinned = signal(true);
  /** Output landed while the user was scrolled away from the tail. */
  readonly hasNew = signal(false);
  readonly mentionQuery = signal<string | null>(null);
  /** Hides the /command menu until the composer is next typed in or clicked. */
  readonly commandMenuDismissed = signal(false);

  readonly hasDraft: ReadSignal<boolean> = computed(
    () => this.draft().trim().length > 0 || this.attachments().length > 0,
  );

  isToolExpanded(id: string): boolean {
    return this.expandedTools().has(id);
  }

  toggleTool(id: string): void {
    this.expandedTools.set((current) => {
      const next = new Set(current);
      if (!next.delete(id)) next.add(id);
      return next;
    });
  }

  openOverlay(overlay: Overlay): void {
    this.overlay.set((current) => (current === overlay ? 'none' : overlay));
  }

  closeOverlay(): void {
    this.overlay.set('none');
  }

  dismissMenus(): void {
    if (IS_MENU[this.overlay()]) this.overlay.set('none');
    this.mentionQuery.set(null);
    this.commandMenuDismissed.set(true);
  }

  addAttachment(attachment: Attachment): void {
    this.attachments.set((list) => [...list, attachment]);
  }

  removeAttachment(id: string): void {
    this.attachments.set((list) => list.filter((attachment) => attachment.id !== id));
  }

  toggleContext(item: ContextItem): void {
    this.pickedContext.set((list) =>
      list.some((picked) => picked.id === item.id)
        ? list.filter((picked) => picked.id !== item.id)
        : [...list, item],
    );
  }

  clearComposer(): void {
    this.draft.set('');
    this.attachments.set([]);
    this.pickedContext.set([]);
    this.mentionQuery.set(null);
  }
}
