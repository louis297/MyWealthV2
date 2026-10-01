import { useEffect } from "react";
import { Navigate, Outlet, useLocation } from "react-router";
import { useAppDispatch } from "@/app/hooks";
import { isSignOutInProgress, startLogin } from "@/features/session/session";
import { roles, shouldLoadTenant } from "@/features/session/roles";
import { setCurrentUser, setTenant } from "@/features/session/sessionSlice";
import { useGetMeQuery, useGetTenantByCodeQuery } from "@/shared/api/api";

export function RequireSession() {
  const dispatch = useAppDispatch();
  const location = useLocation();
  const { data: me, isLoading, isError } = useGetMeQuery();

  useEffect(() => {
    if (me) {
      dispatch(setCurrentUser(me));
    }
  }, [me, dispatch]);

  const loadTenant = shouldLoadTenant(me?.role, me?.tenantCode);
  const { data: tenant } = useGetTenantByCodeQuery(me?.tenantCode ?? "", { skip: !loadTenant });

  useEffect(() => {
    if (tenant) {
      dispatch(setTenant(tenant));
    }
  }, [tenant, dispatch]);

  useEffect(() => {
    if (isError && !isSignOutInProgress()) {
      const path = `${location.pathname}${location.search}`;
      startLogin(path);
    }
  }, [isError, location.pathname, location.search]);

  if (isLoading) {
    return <p>Loading session…</p>;
  }

  if (isError || !me) {
    return <p>Loading session…</p>;
  }

  if (me.role === roles.customer) {
    if (location.pathname === "/forbidden") {
      return <Outlet />;
    }
    return <Navigate to="/forbidden" replace />;
  }

  return <Outlet />;
}
