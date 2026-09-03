import { el, when } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import type { PanelContext } from './context.js';
import { icon, type IconName } from './icons.js';

const STARTERS: readonly { icon: IconName; text: string }[] = [
  { icon: 'cube', text: 'What is selected, and what would you change about it?' },
  { icon: 'graph', text: 'Build a parametric facade panel in Grasshopper' },
  { icon: 'terminal', text: 'Write a script that renames layers to match their parent' },
  { icon: 'camera', text: 'Set up a three-quarter view and capture it' },
  { icon: 'document', text: 'Audit this model for open breps and tiny edges' },
];

export function emptyState(ctx: PanelContext): Child {
  return when(
    () => ctx.store.hasReadyAgent(),
    () =>
      el(
        'div',
        { class: 'empty' },
        el(
          'div',
          { class: 'empty-title' },
          icon('sparkle', 17),
          el('span', { text: () => `${ctx.store.activeAgent()?.label ?? 'The agent'} is ready` }),
        ),
        el('p', {
          text: 'It can read the document, run scripts, drive Grasshopper and capture views. Mention @context to point it at something specific.',
        }),
        el(
          'div',
          { class: 'starters' },
          ...STARTERS.map((starter) =>
            el(
              'button',
              { class: 'starter', type: 'button', onClick: () => ctx.submit(starter.text) },
              icon(starter.icon, 15),
              el('span', { text: starter.text }),
            ),
          ),
        ),
      ),
    () =>
      el(
        'div',
        { class: 'empty' },
        el('div', { class: 'empty-title' }, icon('agent', 17), el('span', { text: 'No agent available' })),
        el('p', { text: 'Install Claude Code, Codex or Gemini CLI and sign in, then pick it here.' }),
        el(
          'div',
          { class: 'starters' },
          el(
            'button',
            { class: 'starter', type: 'button', onClick: () => ctx.send({ type: 'settings.open' }) },
            icon('settings', 15),
            el('span', { text: 'Open AI settings' }),
          ),
          el(
            'button',
            {
              class: 'starter',
              type: 'button',
              onClick: () => ctx.openLink('https://developer.rhino3d.com/guides/rhinoai/getting-started/'),
            },
            icon('reveal', 15),
            el('span', { text: 'Read the setup guide' }),
          ),
        ),
      ),
  );
}
