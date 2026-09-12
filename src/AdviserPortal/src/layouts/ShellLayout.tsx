import type { ReactNode } from "react";

export function ShellLayout({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <header className="border-b border-slate-200 bg-white px-4 py-2 text-sm">
        Adviser Portal
      </header>
      <main className="p-4">{children}</main>
    </div>
  );
}
