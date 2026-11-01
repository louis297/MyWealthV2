import { Navigate } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { isCustomer } from "@/features/session/roles";

export function HomePage() {
  const role = useAppSelector((state) => state.session.currentUser?.role);
  if (!role) {
    return <p>Loading session…</p>;
  }

  if (isCustomer(role)) {
    return <Navigate to="/dashboard" replace />;
  }

  return <Navigate to="/forbidden" replace />;
}
