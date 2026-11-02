import { useEffect } from "react";
import { Outlet, useLocation } from "react-router";
import { useAppDispatch } from "@/app/hooks";
import { isSignOutInProgress, startLogin } from "@/features/session/session";
import { setCurrentUser } from "@/features/session/sessionSlice";
import { Muted } from "@/components/portal";
import { useGetMeQuery } from "@/shared/api/api";

export function RequireSession() {
  const dispatch = useAppDispatch();
  const location = useLocation();
  const { data: me, isLoading, isError } = useGetMeQuery();

  useEffect(() => {
    if (me) {
      dispatch(setCurrentUser(me));
    }
  }, [me, dispatch]);

  useEffect(() => {
    if (isError && !isSignOutInProgress()) {
      const path = `${location.pathname}${location.search}`;
      startLogin(path);
    }
  }, [isError, location.pathname, location.search]);

  if (isLoading || isError || !me) {
    return <Muted>Loading session…</Muted>;
  }

  return <Outlet />;
}
