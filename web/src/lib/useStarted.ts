import { useEffect, useState } from 'react';

/** Browsers cap timer delays at about 24.8 days; a longer wait simply re-arms. */
const MAX_DELAY = 2_147_000_000;

/**
 * Whether the instant has been reached. Re-checks when it arrives, so a page left open notices (for example, a lesson
 * can be marked as held once it starts). The delay is measured from the real current time whenever the instant changes,
 * never from an older render.
 */
export function useStarted(startUtc: string): boolean {
  const start = new Date(startUtc).getTime();
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (now >= start) return;
    const remaining = start - Date.now();
    const timer = setTimeout(() => setNow(Date.now()), Math.min(Math.max(remaining + 50, 0), MAX_DELAY));
    return () => clearTimeout(timer);
  }, [now, start]);
  return now >= start;
}
