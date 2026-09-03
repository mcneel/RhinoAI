import { bind, each, el, when } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { computed, signal } from '../core/signal.js';
import { formatBytes } from '../state/format.js';
import type { Attachment, ContextItem } from '../protocol/events.js';
import type { PanelContext } from './context.js';
import { icon, type IconName } from './icons.js';

interface Command {
  key: string;
  label: string;
  hint: string;
  icon: IconName;
  run(ctx: PanelContext): void;
}

const COMMANDS: readonly Command[] = [
  { key: 'new', label: '/new', hint: 'Start a fresh conversation', icon: 'plus', run: (c) => c.send({ type: 'conversation.new' }) },
  { key: 'history', label: '/history', hint: 'Browse past conversations', icon: 'history', run: (c) => c.ui.openOverlay('history') },
  { key: 'agent', label: '/agent', hint: 'Switch agent or model', icon: 'agent', run: (c) => c.ui.openOverlay('agents') },
  { key: 'stop', label: '/stop', hint: 'Cancel the running turn', icon: 'stop', run: (c) => c.send({ type: 'cancel' }) },
  { key: 'settings', label: '/settings', hint: 'Open AI settings', icon: 'settings', run: (c) => c.send({ type: 'settings.open' }) },
];

const CONTEXT_ICON: Record<ContextItem['kind'], IconName> = {
  selection: 'cube',
  layer: 'layers',
  view: 'camera',
  document: 'document',
  block: 'cube',
  grasshopper: 'graph',
  file: 'document',
};

async function readDropped(files: readonly File[]): Promise<Attachment[]> {
  const read = files.map(
    (file, index) =>
      new Promise<Attachment>((resolve, reject) => {
        const reader = new FileReader();
        reader.onerror = () => reject(reader.error);
        reader.onload = () =>
          resolve({
            id: `local-${Date.now()}-${index}`,
            kind: file.type.startsWith('image/') ? 'image' : 'text',
            name: file.name,
            mediaType: file.type || 'application/octet-stream',
            bytes: file.size,
            ...(file.type.startsWith('image/') ? { dataUrl: String(reader.result) } : {}),
          });
        if (file.type.startsWith('image/')) reader.readAsDataURL(file);
        else reader.readAsText(file);
      }),
  );
  return Promise.all(read);
}

export function composer(ctx: PanelContext): Child {
  const { store, ui } = ctx;
  const dropping = signal(false);
  const highlighted = signal(0);

  let input!: HTMLTextAreaElement;

  const grow = (): void => {
    input.style.height = 'auto';
    input.style.height = `${input.scrollHeight}px`;
  };

  // /command while the draft is a single leading token; @mention while the caret sits in one.
  const slashQuery = computed<string | null>(() => {
    const draft = ui.draft();
    const match = /^\/([a-z]*)$/.exec(draft);
    return match ? (match[1] as string) : null;
  });

  const commandMatches = computed<readonly Command[]>(() => {
    const query = slashQuery();
    if (query === null) return [];
    return COMMANDS.filter((command) => command.key.startsWith(query));
  });

  const contextMatches = computed<readonly ContextItem[]>(() => {
    const query = ui.mentionQuery();
    if (query === null) return [];
    const lower = query.toLowerCase();
    return store
      .context()
      .filter((item) => item.label.toLowerCase().includes(lower))
      .filter((item) => !ui.pickedContext().some((picked) => picked.id === item.id));
  });

  const menuOpen = computed(() => commandMatches().length > 0 || contextMatches().length > 0);
  bind(() => {
    menuOpen();
    highlighted.set(0);
  });

  const syncMention = (): void => {
    const caret = input.selectionStart ?? 0;
    const before = ui.draft().slice(0, caret);
    const match = /(?:^|\s)@([\w -]*)$/.exec(before);
    ui.mentionQuery.set(match ? (match[1] as string) : null);
  };

  const pickContext = (item: ContextItem): void => {
    ui.toggleContext(item);
    const caret = input.selectionStart ?? 0;
    const draft = ui.draft();
    const before = draft.slice(0, caret).replace(/@[\w -]*$/, '');
    ui.draft.set(before + draft.slice(caret));
    ui.mentionQuery.set(null);
    requestAnimationFrame(() => {
      input.focus();
      input.setSelectionRange(before.length, before.length);
      grow();
    });
  };

  const runCommand = (command: Command): void => {
    ui.draft.set('');
    command.run(ctx);
    requestAnimationFrame(grow);
  };

  const acceptHighlighted = (): boolean => {
    const commands = commandMatches();
    if (commands.length > 0) {
      runCommand(commands[Math.min(highlighted(), commands.length - 1)] as Command);
      return true;
    }
    const items = contextMatches();
    if (items.length > 0) {
      pickContext(items[Math.min(highlighted(), items.length - 1)] as ContextItem);
      return true;
    }
    return false;
  };

  const onKeyDown = (event: KeyboardEvent): void => {
    if (menuOpen() && (event.key === 'ArrowDown' || event.key === 'ArrowUp')) {
      event.preventDefault();
      const size = Math.max(commandMatches().length, contextMatches().length);
      highlighted.set((current) => (current + (event.key === 'ArrowDown' ? 1 : size - 1)) % size);
      return;
    }
    if (event.key === 'Escape') {
      if (menuOpen()) {
        ui.mentionQuery.set(null);
        if (slashQuery() !== null) ui.draft.set('');
        event.preventDefault();
        return;
      }
      if (store.running()) {
        ctx.send({ type: 'cancel' });
        event.preventDefault();
      }
      return;
    }
    if (event.key === 'Enter' && !event.shiftKey) {
      event.preventDefault();
      if (acceptHighlighted()) return;
      if (store.running()) return;
      ctx.submit();
      requestAnimationFrame(grow);
    }
  };

  const onPaste = (event: ClipboardEvent): void => {
    const files = [...(event.clipboardData?.items ?? [])]
      .filter((item) => item.kind === 'file')
      .map((item) => item.getAsFile())
      .filter((file): file is File => file !== null);
    if (files.length === 0) return;
    event.preventDefault();
    void readDropped(files).then((attachments) => attachments.forEach((a) => ui.addAttachment(a)));
  };

  const attachmentTile = (attachment: Attachment): Child =>
    el(
      'div',
      { class: 'attach' },
      attachment.dataUrl
        ? el('img', { src: attachment.dataUrl, alt: attachment.name })
        : el('span', { class: 'glyph' }, icon('document', 14)),
      el(
        'span',
        { class: 'meta' },
        el('b', { text: attachment.name }),
        el('span', { text: formatBytes(attachment.bytes) }),
      ),
      el(
        'button',
        { class: 'icon-btn', type: 'button', 'aria-label': `Remove ${attachment.name}`, onClick: () => ui.removeAttachment(attachment.id) },
        icon('close', 12),
      ),
    );

  const menu = (): Child => {
    if (!menuOpen()) return null;
    const commands = commandMatches();
    if (commands.length > 0)
      return el(
        'div',
        { class: 'mention-menu', role: 'listbox' },
        el('div', { class: 'menu-head', text: 'Commands' }),
        ...commands.map((command, index) =>
          el(
            'button',
            {
              class: () => `menu-item${index === highlighted() ? ' selected' : ''}`,
              type: 'button',
              onMouseEnter: () => highlighted.set(index),
              onClick: () => runCommand(command),
            },
            icon(command.icon, 14),
            el('span', { class: 'body' }, el('b', { text: command.label }), el('span', { text: command.hint })),
          ),
        ),
      );

    const items = contextMatches();
    return el(
      'div',
      { class: 'mention-menu', role: 'listbox' },
      el('div', { class: 'menu-head', text: 'Attach document context' }),
      ...items.map((item, index) =>
        el(
          'button',
          {
            class: () => `menu-item${index === highlighted() ? ' selected' : ''}`,
            type: 'button',
            onMouseEnter: () => highlighted.set(index),
            onClick: () => pickContext(item),
          },
          icon(CONTEXT_ICON[item.kind], 14),
          el(
            'span',
            { class: 'body' },
            el('b', { text: item.count !== undefined ? `${item.label} (${item.count})` : item.label }),
            el('span', { text: item.detail ?? item.kind }),
          ),
        ),
      ),
    );
  };

  return el(
    'div',
    {
      class: () => `composer${dropping() ? ' dropping' : ''}`,
      onDragOver: (event: DragEvent) => {
        event.preventDefault();
        dropping.set(true);
      },
      onDragLeave: () => dropping.set(false),
      onDrop: (event: DragEvent) => {
        event.preventDefault();
        dropping.set(false);
        const files = [...(event.dataTransfer?.files ?? [])];
        if (files.length > 0)
          void readDropped(files).then((attachments) => attachments.forEach((a) => ui.addAttachment(a)));
      },
    },
    menu,
    when(
      () => ui.pickedContext().length > 0,
      () =>
        el(
          'div',
          { class: 'chip-row' },
          each(
            () => ui.pickedContext(),
            (item) => item.id,
            (item) =>
              el(
                'span',
                { class: 'chip accent' },
                icon(CONTEXT_ICON[item.kind], 12),
                el('span', { text: item.count !== undefined ? `${item.label} (${item.count})` : item.label }),
                el(
                  'button',
                  { type: 'button', 'aria-label': `Remove ${item.label}`, onClick: () => ui.toggleContext(item) },
                  icon('close', 11),
                ),
              ),
          ),
        ),
    ),
    when(
      () => ui.attachments().length > 0,
      () =>
        el(
          'div',
          { class: 'attach-grid' },
          each(
            () => ui.attachments(),
            (attachment) => attachment.id,
            attachmentTile,
          ),
        ),
    ),
    el(
      'div',
      { class: 'composer-box' },
      el('textarea', {
        rows: 1,
        placeholder: () =>
          store.running() ? 'Type your next message…' : 'Ask, or describe what you want built.  / for commands, @ for context',
        'aria-label': 'Message',
        value: () => ui.draft(),
        onInput: (event: Event) => {
          ui.draft.set((event.target as HTMLTextAreaElement).value);
          syncMention();
          grow();
        },
        onKeyDown,
        onPaste,
        onClick: syncMention,
        ref: (node: HTMLTextAreaElement) => {
          input = node;
          requestAnimationFrame(() => {
            grow();
            node.focus();
          });
        },
      }),
      el(
        'div',
        { class: 'composer-actions' },
        el(
          'button',
          { class: 'icon-btn', type: 'button', title: 'Attach a file', onClick: () => ctx.send({ type: 'attachments.pick' }) },
          icon('paperclip', 15),
        ),
        el(
          'button',
          {
            class: 'icon-btn',
            type: 'button',
            title: 'Attach document context',
            onClick: () => {
              const draft = ui.draft();
              const needsSpace = draft.length > 0 && !draft.endsWith(' ');
              ui.draft.set(`${draft}${needsSpace ? ' ' : ''}@`);
              ui.mentionQuery.set('');
              requestAnimationFrame(() => {
                input.focus();
                input.setSelectionRange(input.value.length, input.value.length);
              });
            },
          },
          icon('at', 15),
        ),
        el('span', { class: 'spacer' }),
        el('span', {
          class: 'composer-hint',
          text: () => (store.running() ? 'Esc to stop' : ui.hasDraft() ? 'Enter to send' : ''),
        }),
        () =>
          store.running()
            ? el(
                'button',
                { class: 'send stop', type: 'button', title: 'Stop  (Esc)', onClick: () => ctx.send({ type: 'cancel' }) },
                icon('stop', 13),
              )
            : el(
                'button',
                {
                  class: 'send',
                  type: 'button',
                  title: 'Send  (Enter)',
                  disabled: () => !ui.hasDraft() || !store.hasReadyAgent(),
                  onClick: () => {
                    ctx.submit();
                    requestAnimationFrame(grow);
                  },
                },
                icon('send', 15),
              ),
      ),
    ),
  );
}
