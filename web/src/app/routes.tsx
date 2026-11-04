import type { RouteObject } from 'react-router';
import { HomePage } from './HomePage.tsx';
import { NotFoundPage } from './NotFoundPage.tsx';

/** Application routes. Area layouts (/app, /student, /admin, auth) are added in R0-P6. */
export const routes: RouteObject[] = [
  { path: '/', element: <HomePage /> },
  { path: '*', element: <NotFoundPage /> },
];
