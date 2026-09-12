import type { StringKey } from '../i18n/strings.js';

export const BUSY_KEYS: readonly StringKey[] = [
  'busy.mulling',
  'busy.grazing',
  'busy.ruminating',
  'busy.chewing',
  'busy.foraging',
  'busy.rummaging',
  'busy.scouting',
  'busy.tracking',
  'busy.ferreting',
  'busy.nosing',
  'busy.digging',
  'busy.burrowing',
  'busy.wading',
  'busy.trekking',
  'busy.roaming',
  'busy.ambling',
  'busy.plodding',
  'busy.trotting',
  'busy.galloping',
  'busy.thundering',
  'busy.herding',
  'busy.gathering',
  'busy.mustering',
  'busy.corralling',
  'busy.beavering',
  'busy.honing',
  'busy.whirring',
];

export const WORD_MS = 4000;

export function busyWord(elapsedMs: number, offset = 0, keys: readonly StringKey[] = BUSY_KEYS): StringKey | null {
  if (keys.length === 0) return null;
  const step = Math.floor(Math.max(0, elapsedMs) / WORD_MS);
  const index = (((offset + step) % keys.length) + keys.length) % keys.length;
  return keys[index] as StringKey;
}
