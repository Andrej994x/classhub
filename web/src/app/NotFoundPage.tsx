import { Link } from 'react-router';

export function NotFoundPage() {
  return (
    <main className="mx-auto flex min-h-screen max-w-2xl flex-col justify-center gap-4 px-4">
      <h1 className="text-2xl font-semibold text-slate-900">404</h1>
      <p className="text-slate-600">Страницата не е пронајдена · Page not found</p>
      <Link className="text-blue-700 underline" to="/">
        ClassHub
      </Link>
    </main>
  );
}
