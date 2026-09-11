import { signal } from '../core/signal.js';
import { EN, type StringKey } from './strings.js';

// A signal, because the table lands on hello, which is after the app has already mounted.
const table = signal<Partial<Record<StringKey, string>>>({});
const tag = signal('en-US');

export function applyStrings(language: string, strings: Readonly<Record<string, string>>): void {
  document.documentElement.lang = language;
  tag.set(language);
  table.set(strings as Partial<Record<StringKey, string>>);
}

export function locale(): string {
  return tag();
}

export function t(key: StringKey, ...args: readonly (string | number)[]): string {
  const text = table()[key] ?? EN[key];
  if (args.length === 0) return text;
  return text.replace(/\{(\d+)\}/g, (marker, index: string) => {
    const value = args[Number(index)];
    return value === undefined ? marker : String(value);
  });
}
