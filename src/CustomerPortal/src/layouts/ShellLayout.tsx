import { NavLink, Outlet } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { startEndSession } from "@/features/session/session";

const links = [
  { to: "/dashboard", label: "Dashboard" },
  { to: "/accounts", label: "Accounts" },
  { to: "/reports", label: "Reports" },
  { to: "/profile", label: "Profile" },
];

export function ShellLayout() {
  const name = useAppSelector((state) => state.session.currentUser?.name);

  return (
    <div className="min-h-screen bg-background text-foreground">
      <header className="flex items-center justify-between border-b px-4 py-3">
        <p className="font-semibold">Customer Portal</p>
        <div className="flex items-center gap-3">
          {name ? <p>{name}</p> : null}
          <button type="button" onClick={() => void startEndSession()}>Sign out</button>
        </div>
      </header>
      <div className="grid grid-cols-[12rem_1fr]">
        <nav aria-label="Primary" className="border-r p-3">
          <ul className="flex flex-col gap-2">
            {links.map((link) => (
              <li key={link.to}>
                <NavLink to={link.to}>{link.label}</NavLink>
              </li>
            ))}
          </ul>
        </nav>
        <main className="p-4">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
