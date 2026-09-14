import { useEffect } from "react";
import { Navigate } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { startAuthorize } from "@/features/session/oidc";
import { canAccessCustomers } from "@/features/session/roles";

export function HomePage() {
  const accessToken = useAppSelector((state) => state.session.accessToken);
  const role = useAppSelector((state) => state.session.currentUser?.role);

  useEffect(() => {
    if (!accessToken) {
      void startAuthorize();
    }
  }, [accessToken]);

  if (!accessToken) {
    return <p>Redirecting to sign in…</p>;
  }

  if (!role) {
    return <p>Loading session…</p>;
  }

  if (canAccessCustomers(role)) {
    return <Navigate to="/customers" replace />;
  }

  return <Navigate to="/profile" replace />;
}
