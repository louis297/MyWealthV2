import { Navigate } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { canAccessDashboard } from "@/features/session/roles";

export function HomePage() {
  const role = useAppSelector((state) => state.session.currentUser?.role);

  if (!role) {
    return <p>Loading session…</p>;
  }

  if (canAccessDashboard(role)) {
    return <Navigate to="/dashboard" replace />;
  }

  return <Navigate to="/profile" replace />;
}
