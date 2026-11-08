import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useStarted } from './useStarted.ts';

const at = (ms: number) => new Date(ms).toISOString();

describe('useStarted', () => {
  afterEach(() => vi.useRealTimers());

  it('turns true when the start arrives', () => {
    vi.useFakeTimers({ now: Date.UTC(2030, 10, 12, 16, 0) });
    const start = Date.now() + 60_000;
    const { result } = renderHook(() => useStarted(at(start)));
    expect(result.current).toBe(false);
    act(() => vi.advanceTimersByTime(60_100));
    expect(result.current).toBe(true);
  });

  it('measures a new start from the real current time, not from when the page opened', () => {
    vi.useFakeTimers({ now: Date.UTC(2030, 10, 12, 16, 0) });
    const opened = Date.now();
    const { result, rerender } = renderHook(({ start }) => useStarted(at(start)), { initialProps: { start: opened - 3_600_000 } });
    expect(result.current).toBe(true);

    // An hour later (no timers pending), the lesson moves five minutes into the future.
    vi.setSystemTime(opened + 3_600_000);
    rerender({ start: opened + 3_600_000 + 300_000 });
    expect(result.current).toBe(false);
    act(() => vi.advanceTimersByTime(300_100));
    expect(result.current).toBe(true);
  });

  it('notices a start that is already past when the stored time is stale', () => {
    vi.useFakeTimers({ now: Date.UTC(2030, 10, 12, 16, 0) });
    const opened = Date.now();
    const { result, rerender } = renderHook(({ start }) => useStarted(at(start)), { initialProps: { start: opened + 7_200_000 } });
    vi.setSystemTime(opened + 3_600_000);
    rerender({ start: opened + 1_800_000 }); // moved to half an hour ago
    act(() => vi.advanceTimersByTime(100));
    expect(result.current).toBe(true);
  });
});
