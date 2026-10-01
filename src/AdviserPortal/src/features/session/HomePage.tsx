import { Navigate } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { canAccessCustomers } from "@/features/session/roles";

export function HomePage() {
  const role = useAppSelector((state) => state.session.currentUser?.role);

  if (!role) {
    return <p>Loading session…</p>;
  }

  if (canAccessCustomers(role)) {
    return <Navigate to="/customers" replace />;
  }

  return <Navigate to="/profile" replace />;
}
