import { el } from '../core/dom.js';
import type { Child } from '../core/dom.js';
import { signal } from '../core/signal.js';
import type { PendingQuestion } from '../protocol/events.js';
import type { PanelContext } from './context.js';
import { icon } from './icons.js';

/**
 * The inline answer for an `ask_user` call. Unlike the Eto card this is a real form: labels are
 * clickable, the radio group is a radio group, Enter submits, Escape dismisses.
 */
export function questionCard(ctx: PanelContext, question: PendingQuestion): Child {
  const multi = question.mode === 'multi';
  const chosen = signal<ReadonlySet<string>>(new Set<string>());
  const other = signal('');

  const answers = (): string[] => {
    const picked = question.options.filter((option) => chosen().has(option));
    const free = other().trim();
    return free ? [...picked, free] : picked;
  };

  const submit = () => {
    const values = answers();
    if (values.length === 0) {
      ctx.send({ type: 'question.dismiss', id: question.id });
      return;
    }
    ctx.send({ type: 'question.answer', id: question.id, answers: values });
  };

  const toggle = (option: string) => {
    chosen.set((current) => {
      if (!multi) return new Set([option]);
      const next = new Set(current);
      if (!next.delete(option)) next.add(option);
      return next;
    });
  };

  const options = question.options.map((option) =>
    el(
      'label',
      null,
      el('input', {
        type: multi ? 'checkbox' : 'radio',
        name: `q-${question.id}`,
        checked: () => chosen().has(option),
        onChange: () => toggle(option),
      }),
      el('span', { text: option }),
    ),
  );

  return el(
    'div',
    {
      class: 'question',
      onKeyDown: (event: KeyboardEvent) => {
        if (event.key === 'Enter' && !event.shiftKey) {
          event.preventDefault();
          submit();
        }
        if (event.key === 'Escape') ctx.send({ type: 'question.dismiss', id: question.id });
      },
    },
    el('h4', null, icon('question', 15), el('span', { text: question.question })),
    el('div', { class: 'opts' }, ...options),
    question.allowOther
      ? el('input', {
          type: 'text',
          placeholder: question.options.length > 0 ? 'Something else…' : 'Your answer…',
          value: () => other(),
          onInput: (event: Event) => other.set((event.target as HTMLInputElement).value),
        })
      : null,
    el(
      'div',
      { class: 'row' },
      el(
        'button',
        {
          class: 'btn',
          type: 'button',
          onClick: () => ctx.send({ type: 'question.dismiss', id: question.id }),
        },
        'Skip',
      ),
      el(
        'button',
        {
          class: 'btn primary',
          type: 'button',
          disabled: () => answers().length === 0,
          onClick: submit,
        },
        'Answer',
      ),
    ),
  );
}
