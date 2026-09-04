import { el } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import type { PendingQuestion } from '../protocol/events.js';
import type { PanelContext } from './context.js';
import { icon } from './icons.js';

const DONT_KNOW = "I don't know";

interface Draft {
  picked: Set<string>;
  other: string;
  dontKnow: boolean;
}

function setText(node: HTMLElement, text: string): void {
  if (node.textContent !== text) node.textContent = text;
}

export function questionOverlay(ctx: PanelContext): Child {
  return () => {
    const questions = ctx.store.questions();
    return questions.length > 0 ? overlay(ctx, questions) : null;
  };
}

function overlay(ctx: PanelContext, questions: readonly PendingQuestion[]): Child {
  const drafts: Draft[] = questions.map(() => ({ picked: new Set<string>(), other: '', dontKnow: false }));
  const isSeries = questions.length > 1;
  let page = 0;

  const answersFor = (index: number): string[] => {
    const draft = drafts[index] as Draft;
    if (draft.dontKnow) return [DONT_KNOW];
    const question = questions[index] as PendingQuestion;
    const picked = question.options.filter((option) => draft.picked.has(option));
    const free = draft.other.trim();
    return free ? [...picked, free] : picked;
  };

  const isPageAnswered = () => answersFor(page).length > 0;
  const isLastPage = () => page === questions.length - 1;

  const cancelAll = () => ctx.send({ type: 'question.dismiss', ids: questions.map((q) => q.id) });

  const submitAll = () => {
    if (!isPageAnswered() || !isLastPage()) return;
    ctx.send({
      type: 'question.answer',
      items: questions.map((question, i) => ({ id: question.id, answers: answersFor(i) })),
    });
  };

  const stepBars = questions.map(() => el('i'));
  const steps = el('div', { class: 'steps', 'aria-hidden': 'true' }, ...stepBars);
  const count = el('span', { class: 'ask-count' });
  const body = el('div', { class: 'ask-body' });
  const back = el('button', { class: 'btn', type: 'button', onClick: () => turnPage(-1) }, 'Back');
  const next = el('button', {
    class: 'btn primary',
    type: 'button',
    onClick: () => (isLastPage() ? submitAll() : turnPage(1)),
  });

  let optionInputs: { input: HTMLInputElement; option: string }[] = [];
  let dontKnowInput: HTMLInputElement | null = null;
  let otherInput: HTMLInputElement | null = null;

  function buildPage(): HTMLElement {
    const question = questions[page] as PendingQuestion;
    const draft = drafts[page] as Draft;
    const isMulti = question.mode === 'multi';
    optionInputs = [];

    const opts = el('div', { class: 'opts' });
    for (const option of question.options) {
      const input = el('input', {
        type: isMulti ? 'checkbox' : 'radio',
        name: `q-${question.id}`,
        checked: draft.picked.has(option),
        onChange: () => {
          draft.dontKnow = false;
          if (isMulti) {
            if (!draft.picked.delete(option)) draft.picked.add(option);
          } else {
            draft.picked = new Set([option]);
          }
          syncControlsInPlace();
        },
      });
      optionInputs.push({ input, option });
      opts.appendChild(el('label', null, input, el('span', { text: option })));
    }

    dontKnowInput = el('input', {
      type: isMulti ? 'checkbox' : 'radio',
      name: `q-${question.id}`,
      checked: draft.dontKnow,
      onChange: () => {
        draft.dontKnow = !draft.dontKnow;
        if (draft.dontKnow) {
          draft.picked = new Set();
          draft.other = '';
        }
        syncControlsInPlace();
      },
    });

    otherInput = question.allowOther
      ? el('input', {
          type: 'text',
          placeholder: question.options.length > 0 ? 'Something else…' : 'Your answer…',
          value: draft.other,
          onInput: (event: Event) => {
            draft.other = (event.target as HTMLInputElement).value;
            if (draft.other.trim()) draft.dontKnow = false;
            syncControlsInPlace();
          },
        })
      : null;

    return el(
      'div',
      { class: 'ask-page turning' },
      el('h4', { id: 'ask-title', text: question.question }),
      opts,
      el(
        'div',
        { class: 'synth' },
        el('label', { class: draft.dontKnow ? 'on' : '' }, dontKnowInput, el('span', { text: DONT_KNOW })),
        otherInput,
      ),
    );
  }

  function syncChrome(): void {
    stepBars.forEach((bar, i) => {
      bar.className = answersFor(i).length > 0 ? 'done' : i === page ? 'here' : '';
    });
    steps.hidden = !isSeries;
    setText(count, isSeries ? `Question ${page + 1} of ${questions.length}` : '');
    back.hidden = !isSeries || page === 0;
    setText(next, isLastPage() ? (isSeries ? 'Answer all' : 'Answer') : 'Next');
    next.disabled = !isPageAnswered();
  }

  function syncControlsInPlace(): void {
    const draft = drafts[page] as Draft;
    for (const { input, option } of optionInputs) input.checked = draft.picked.has(option);
    if (dontKnowInput) {
      dontKnowInput.checked = draft.dontKnow;
      dontKnowInput.closest('label')?.setAttribute('class', draft.dontKnow ? 'on' : '');
    }
    if (otherInput && otherInput.value !== draft.other) otherInput.value = draft.other;
    syncChrome();
  }

  function easeBodyHeight(from: number): void {
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
    const to = body.scrollHeight;
    if (Math.abs(to - from) <= 1) return;
    body.classList.add('animating');
    body.style.height = `${from}px`;
    void body.offsetHeight;
    body.style.height = `${to}px`;
  }

  function focusCard(): void {
    if (card.isConnected) card.focus({ preventScroll: true });
    else requestAnimationFrame(() => card.focus({ preventScroll: true }));
  }

  function showPage(previousHeight: number | null): void {
    body.replaceChildren(buildPage());
    syncChrome();
    if (previousHeight !== null) easeBodyHeight(previousHeight);
    focusCard();
  }

  function turnPage(delta: number): void {
    const target = Math.min(Math.max(page + delta, 0), questions.length - 1);
    if (target === page) return;
    page = target;
    showPage(body.getBoundingClientRect().height);
  }

  function keepTabInsideCard(event: KeyboardEvent): void {
    const focusable = [...card.querySelectorAll<HTMLElement>('button:not(:disabled), input')].filter(
      (node) => node.offsetParent !== null,
    );
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (!first || !last) return;
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  body.addEventListener('transitionend', (event: TransitionEvent) => {
    if (event.propertyName !== 'height') return;
    body.style.height = '';
    body.classList.remove('animating');
  });

  // Rebuilding the card per page would replay its entry animation, so pages swap inside it.
  const card = el(
    'div',
    {
      class: 'ask',
      role: 'dialog',
      'aria-modal': 'true',
      'aria-labelledby': 'ask-title',
      tabindex: '-1',
      onKeyDown: (event: KeyboardEvent) => {
        if (event.key === 'Enter' && !event.shiftKey) {
          event.preventDefault();
          if (!isPageAnswered()) return;
          if (isLastPage()) submitAll();
          else turnPage(1);
        } else if (event.key === 'Escape') {
          event.preventDefault();
          cancelAll();
        } else if (event.key === 'Tab') {
          keepTabInsideCard(event);
        }
      },
    },
    el(
      'div',
      { class: 'ask-head' },
      el('span', { class: 'glyph', 'aria-hidden': 'true' }, icon('question', 12)),
      steps,
      el('span', { class: 'spacer' }),
      count,
    ),
    body,
    el(
      'div',
      { class: 'ask-row' },
      el('button', { class: 'btn ghost', type: 'button', onClick: cancelAll }, 'Cancel'),
      el('span', { class: 'spacer' }),
      back,
      next,
    ),
  );

  showPage(null);
  return el('div', { class: 'ask-scrim' }, card);
}
