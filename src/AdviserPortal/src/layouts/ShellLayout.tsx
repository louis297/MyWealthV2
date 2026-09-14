import type { ReactNode } from "react";
import { NavLink, Outlet } from "react-router";
import { useAppSelector } from "@/app/hooks";
import {
  canAccessAdvisers,
  canAccessCustomers,
  canAccessProfile,
  roles,
} from "@/features/session/roles";

export function ShellLayout({ children }: { children?: ReactNode }) {
  const me = useAppSelector((state) => state.session.currentUser);
  const tenant = useAppSelector((state) => state.session.tenant);
  const firmName = me?.role === roles.systemAdmin ? "Platform" : tenant?.name;

  return (
    <div className="min-h-screen bg-slate-50 text-slate-900">
      <header className="flex flex-wrap items-center gap-x-4 gap-y-1 border-b border-slate-200 bg-white px-4 py-2 text-sm">
        <span className="font-medium">Adviser Portal</span>
        {firmName ? <span>{firmName}</span> : null}
        {me?.tenantCode ? <span className="text-slate-500">{me.tenantCode}</span> : null}
        {me?.name ? <span>{me.name}</span> : null}
        {me?.role ? <span>{me.role}</span> : null}
        <button type="button" className="ml-auto">
          Sign out
        </button>
      </header>
      <div className="flex min-h-[calc(100vh-2.75rem)]">
        <nav className="w-48 border-r border-slate-200 bg-white p-4 text-sm">
          <ul className="space-y-2">
            {canAccessCustomers(me?.role) ? (
              <li>
                <NavLink to="/customers">Customers</NavLink>
              </li>
            ) : null}
            {canAccessAdvisers(me?.role) ? (
              <li>
                <NavLink to="/advisers">Advisers</NavLink>
              </li>
            ) : null}
            {canAccessProfile(me?.role) ? (
              <li>
                <NavLink to="/profile">Profile</NavLink>
              </li>
            ) : null}
          </ul>
        </nav>
        <main className="flex-1 p-4">{children ?? <Outlet />}</main>
      </div>
    </div>
  );
}
