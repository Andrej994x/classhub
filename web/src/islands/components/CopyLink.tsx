import { useEffect, useState } from 'react';

export interface CopyLinkProps {
  label?: string;
  copied?: string;
}

/** Copies the page address (without query or fragment) to the clipboard. The first island of the public pages. */
export function CopyLink({ label = 'Copy link', copied = 'Link copied' }: CopyLinkProps) {
  const [done, setDone] = useState(false);

  useEffect(() => {
    if (!done) {
      return undefined;
    }

    const timer = window.setTimeout(() => setDone(false), 2000);
    return () => window.clearTimeout(timer);
  }, [done]);

  async function copy() {
    try {
      await navigator.clipboard.writeText(`${window.location.origin}${window.location.pathname}`);
      setDone(true);
    } catch {
      // Clipboard access denied: the address bar still has the link.
    }
  }

  return (
    <button
      type="button"
      onClick={() => void copy()}
      aria-live="polite"
      className="rounded-full border border-site-muted/40 px-4 py-1.5 font-semibold text-site-text hover:bg-site-surface focus:outline-none focus-visible:ring-2 focus-visible:ring-site-primary"
    >
      {done ? copied : label}
    </button>
  );
}
