import { StrictMode, type ComponentType } from 'react';
import { createRoot } from 'react-dom/client';

/**
 * Mounts a React island into every `<div data-island="{name}" data-props="{json}">` that the Razor tag helper
 * `<vite-island>` rendered. Props come from the server as JSON in an attribute (never executed). The page works
 * without the island; it only adds interaction.
 */
export function mountIsland<P extends object>(name: string, Component: ComponentType<P>, root: ParentNode = document): number {
  let mounted = 0;
  for (const element of root.querySelectorAll<HTMLElement>(`[data-island="${name}"]`)) {
    if (element.dataset.mounted === 'true') {
      continue;
    }

    element.dataset.mounted = 'true';
    let props = {} as P;
    try {
      props = JSON.parse(element.dataset.props ?? '{}') as P;
    } catch {
      // Malformed props: render with defaults rather than not at all.
    }

    createRoot(element).render(
      <StrictMode>
        <Component {...props} />
      </StrictMode>,
    );
    mounted++;
  }

  return mounted;
}
