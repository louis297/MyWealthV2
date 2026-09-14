import { Outlet } from "react-router";
import type { Role } from "@/features/session/roles";

export function RoleGate({ allow }: { allow: Role[] }) {
  void allow;
  return <Outlet />;
}
