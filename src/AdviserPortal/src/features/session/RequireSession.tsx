import { useEffect } from "react";
import { Navigate, Outlet, useLocation } from "react-router";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { startAuthorize } from "@/features/session/oidc";
import { roles, shouldLoadTenant } from "@/features/session/roles";
import { setCurrentUser, setTenant } from "@/features/session/sessionSlice";
import { useGetMeQuery, useGetTenantByCodeQuery } from "@/shared/api/api";

export function RequireSession() {
  const accessToken = useAppSelector((state) => state.session.accessToken);
  const dispatch = useAppDispatch();
  const location = useLocation();
  const { data: me, isLoading, isError } = useGetMeQuery(undefined, { skip: !accessToken });

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
    if (!accessToken) {
      void startAuthorize();
    }
  }, [accessToken]);

  if (!accessToken || isLoading) {
    return <p>Loading session…</p>;
  }

  if (isError || !me) {
    return <p>Could not load session.</p>;
  }

  if (me.role === roles.customer) {
    if (location.pathname === "/forbidden") {
      return <Outlet />;
    }
    return <Navigate to="/forbidden" replace />;
  }

  return <Outlet />;
}
