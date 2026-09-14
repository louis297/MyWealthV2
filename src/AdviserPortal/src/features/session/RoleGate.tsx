import { Navigate, Outlet } from "react-router";
import { useAppSelector } from "@/app/hooks";
import type { Role } from "@/features/session/roles";

export function RoleGate({ allow }: { allow: Role[] }) {
  const role = useAppSelector((state) => state.session.currentUser?.role);
  if (!role || !allow.includes(role as Role)) {
    return <Navigate to="/forbidden" replace />;
  }

  return <Outlet />;
}
