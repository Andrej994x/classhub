import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { mountIsland } from '../mount.tsx';
import { CopyLink } from './CopyLink.tsx';

describe('copy-link island', () => {
  it('copies the page address without query or fragment and confirms it', async () => {
    const user = userEvent.setup();
    const writeText = vi.fn<(text: string) => Promise<void>>().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    window.history.replaceState(null, '', '/ana-math?utm=x#contact');

    render(<CopyLink label="Copy link" copied="Link copied" />);
    await user.click(screen.getByRole('button', { name: 'Copy link' }));

    expect(writeText).toHaveBeenCalledWith(`${window.location.origin}/ana-math`);
    expect(await screen.findByRole('button', { name: 'Link copied' })).toBeInTheDocument();
  });

  it('mounts into the server-rendered element with props from data-props, once', async () => {
    document.body.innerHTML = `<div data-island="copy-link" data-props='{"label":"Копирај линк","copied":"Копирано"}'></div><div data-island="copy-link" data-props="not json"></div>`;

    await act(async () => {
      expect(mountIsland('copy-link', CopyLink)).toBe(2);
    });
    expect(mountIsland('copy-link', CopyLink)).toBe(0);

    expect(screen.getByRole('button', { name: 'Копирај линк' })).toBeInTheDocument();
    // Malformed props fall back to the defaults instead of breaking the page.
    expect(screen.getByRole('button', { name: 'Copy link' })).toBeInTheDocument();
  });
});
