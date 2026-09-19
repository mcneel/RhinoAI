// The zoom ladder, on its own so it has no imports and can be unit tested directly.
// The stateful half lives in zoom.ts.

export const STEPS = [0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2] as const;
export const DEFAULT = 1;

/** The CSS zoom for a user-facing level. Rounded, or 1.1 lands on 1.1000000000000001. */
export function toCssZoom(level: number): number {
  return Math.round(level * 1000) / 1000;
}

/** Nearest ladder rung at or below `value`, so an arbitrary stored level still steps sensibly. */
export function indexOf(value: number): number {
  let best = STEPS.indexOf(DEFAULT);
  let distance = Number.POSITIVE_INFINITY;
  for (let i = 0; i < STEPS.length; i++) {
    const gap = Math.abs((STEPS[i] as number) - value);
    if (gap < distance) {
      distance = gap;
      best = i;
    }
  }
  return best;
}

export function stepIn(value: number): number {
  return STEPS[Math.min(indexOf(value) + 1, STEPS.length - 1)] as number;
}

export function stepOut(value: number): number {
  return STEPS[Math.max(indexOf(value) - 1, 0)] as number;
}

export function canZoomIn(value: number): boolean {
  return stepIn(value) !== value;
}

export function canZoomOut(value: number): boolean {
  return stepOut(value) !== value;
}

export function asPercent(value: number): string {
  return `${Math.round(value * 100)}%`;
}
