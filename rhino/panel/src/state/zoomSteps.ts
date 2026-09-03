// The zoom ladder, on its own so it has no imports and can be unit tested directly.
// The stateful half lives in zoom.ts.

export const STEPS = [0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2] as const;
export const DEFAULT = 1;

// The stylesheet is authored a notch large for a docked panel, so the design's natural size is 90%
// of what the CSS literally says. Folding that in here means the user's 100% is the intended size
// and "Reset zoom (100%)" is honest, rather than the default being a peculiar 90%.
const BASE = 0.9;

/** The CSS zoom for a user-facing level. Rounded, or 1.1 x 0.9 lands on 0.9900000000000001. */
export function toCssZoom(level: number): number {
  return Math.round(level * BASE * 1000) / 1000;
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
