import { locale, t } from '../i18n/t.js';

export function relativeTime(iso: string, now = Date.now()): string {
  const then = Date.parse(iso);
  if (Number.isNaN(then)) return '';
  const seconds = Math.max(0, Math.round((now - then) / 1000));
  if (seconds < 45) return t('format.justNow');
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return t('format.minutesAgo', minutes);
  const hours = Math.round(minutes / 60);
  if (hours < 24) return t('format.hoursAgo', hours);
  const days = Math.round(hours / 24);
  if (days < 7) return t('format.daysAgo', days);
  return new Date(then).toLocaleDateString(locale(), { month: 'short', day: 'numeric' });
}

export function clockTime(iso: string): string {
  const then = Date.parse(iso);
  if (Number.isNaN(then)) return '';
  return new Date(then).toLocaleTimeString(locale(), { hour: '2-digit', minute: '2-digit' });
}

export function formatTokens(count: number): string {
  if (count >= 1_000_000) return `${(count / 1_000_000).toFixed(1)}M`;
  if (count >= 1000) return `${(count / 1000).toFixed(count >= 10_000 ? 0 : 1)}k`;
  return String(count);
}

export function formatDuration(ms: number): string {
  if (ms < 1000) return `${Math.round(ms)}ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(ms < 10_000 ? 1 : 0)}s`;
  const minutes = Math.floor(ms / 60_000);
  return `${minutes}m ${Math.round((ms % 60_000) / 1000)}s`;
}

/** A counter that is read while it runs, so it only ever moves a second at a time: "8s", then "1:05". */
export function formatElapsed(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  if (total < 60) return `${total}s`;
  return `${Math.floor(total / 60)}:${String(total % 60).padStart(2, '0')}`;
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function prettyJson(value: unknown): string {
  if (value === undefined) return '';
  if (typeof value === 'string') return value;
  try {
    return JSON.stringify(value, null, 2) ?? '';
  } catch {
    return String(value);
  }
}

/** First meaningful line of a prompt, for a turn header or a history row. */
export function summarize(prompt: string, max = 64): string {
  const line = prompt.split('\n').find((candidate) => candidate.trim().length > 0)?.trim() ?? '';
  return line.length > max ? `${line.slice(0, max).trimEnd()}…` : line;
}
