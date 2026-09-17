import { each, el, when } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { t } from '../i18n/t.js';
import type { StringKey } from '../i18n/strings.js';
import type { HostInfo } from '../protocol/events.js';
import type { PanelContext } from './context.js';
import { icon, type IconName } from './icons.js';

type Profile = NonNullable<HostInfo['profile']>;

// What each assistant offers to start with, in the order they are worth trying: read something
// first, then change something, then the ambitious one.
const STARTERS: Record<Profile, readonly { icon: IconName; key: StringKey }[]> = {
  rhino: [
    { icon: 'terminal', key: 'empty.starterCommand' },
    { icon: 'graph', key: 'empty.starterTower' },
    { icon: 'layers', key: 'empty.starterLayers' },
  ],
  script: [
    { icon: 'python', key: 'empty.starterScriptNew' },
    { icon: 'search', key: 'empty.starterScriptExplain' },
    { icon: 'cube', key: 'empty.starterScriptPick' },
    { icon: 'retry', key: 'empty.starterScriptFix' },
  ],
  grasshopper: [
    { icon: 'graph', key: 'empty.starterGhRead' },
    { icon: 'gh2', key: 'empty.starterGhTower' },
    { icon: 'alert', key: 'empty.starterGhSolve' },
  ],
};

// What each assistant says it is for, under the "ready" line.
const BODY: Record<Profile, StringKey> = {
  rhino: 'empty.body',
  script: 'empty.bodyScript',
  grasshopper: 'empty.bodyGrasshopper',
};

export function emptyState(ctx: PanelContext): Child {
  // The host names the assistant in its `hello`, which can land after this is first built, so the
  // blurb and the starters read it reactively rather than once.
  const profile = (): Profile => ctx.store.host()?.profile ?? 'rhino';

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
          el('span', { text: () => t('empty.ready', ctx.store.activeAgent()?.label ?? t('empty.theAgent')) }),
        ),
        el('p', { text: () => t(BODY[profile()]) }),
        el(
          'div',
          { class: 'starters' },
          each(
            () => STARTERS[profile()],
            (starter) => starter.key,
            (starter) =>
              el(
                'button',
                { class: 'starter', type: 'button', onClick: () => ctx.submit(t(starter.key)) },
                icon(starter.icon, 15),
                el('span', { text: () => t(starter.key) }),
              ),
          ),
        ),
      ),
    () =>
      el(
        'div',
        { class: 'empty' },
        el('div', { class: 'empty-title' }, icon('agent', 17), el('span', { text: () => t('empty.noAgent') })),
        el('p', { text: () => t('empty.noAgentBody') }),
        el(
          'div',
          { class: 'starters' },
          el(
            'button',
            { class: 'starter', type: 'button', onClick: () => ctx.send({ type: 'settings.open' }) },
            icon('settings', 15),
            el('span', { text: () => t('empty.openSettings') }),
          ),
          el(
            'button',
            {
              class: 'starter',
              type: 'button',
              onClick: () => ctx.openLink('https://mcneel.github.io/RhinoAI/'),
            },
            icon('reveal', 15),
            el('span', { text: () => t('empty.setupGuide') }),
          ),
        ),
      ),
  );
}
