import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { createBrowserRouter, RouterProvider } from 'react-router';
import { Providers } from './app/Providers.tsx';
import { routes } from './app/routes.tsx';
import './index.css';
import { captureInstallPrompt, registerServiceWorker } from './pwa/pwa.ts';

const router = createBrowserRouter(routes);

const container = document.getElementById('root');
if (!container) {
  throw new Error('Root element #root not found');
}

registerServiceWorker();
captureInstallPrompt();

createRoot(container).render(
  <StrictMode>
    <Providers>
      <RouterProvider router={router} />
    </Providers>
  </StrictMode>,
);
