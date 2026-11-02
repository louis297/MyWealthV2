import type { ReactNode } from "react";
import { Navigate } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { isCustomer } from "@/features/session/roles";

export function RoleGate({ children }: { children: ReactNode }) {
  const role = useAppSelector((state) => state.session.currentUser?.role);
  if (!role) {
    return null;
  }

  if (!isCustomer(role)) {
    return <Navigate to="/forbidden" replace />;
  }

  return children;
}
