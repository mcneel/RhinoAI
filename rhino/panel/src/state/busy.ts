// No word here may double as a Rhino verb: "Meshing" or "Trimming" would read as a running operation.
export const BUSY_WORDS: readonly string[] = [
  'Mulling',
  'Grazing',
  'Ruminating',
  'Chewing',
  'Foraging',
  'Rummaging',
  'Scouting',
  'Tracking',
  'Ferreting',
  'Nosing',
  'Digging',
  'Burrowing',
  'Wading',
  'Trekking',
  'Roaming',
  'Ambling',
  'Plodding',
  'Trotting',
  'Galloping',
  'Thundering',
  'Herding',
  'Gathering',
  'Mustering',
  'Corralling',
  'Beavering',
  'Honing',
  'Whirring',
];

export const WORD_MS = 4000;

export function busyWord(elapsedMs: number, offset = 0, words: readonly string[] = BUSY_WORDS): string {
  if (words.length === 0) return '';
  const step = Math.floor(Math.max(0, elapsedMs) / WORD_MS);
  const index = (((offset + step) % words.length) + words.length) % words.length;
  return words[index] as string;
}
