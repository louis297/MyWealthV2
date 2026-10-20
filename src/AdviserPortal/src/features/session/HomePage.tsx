import { Navigate } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { Muted } from "@/components/portal";
import { canAccessDashboard } from "@/features/session/roles";

export function HomePage() {
  const role = useAppSelector((state) => state.session.currentUser?.role);

  if (!role) {
    return <Muted>Loading session…</Muted>;
  }

  if (canAccessDashboard(role)) {
    return <Navigate to="/dashboard" replace />;
  }

  return <Navigate to="/profile" replace />;
}
