import { useEffect } from "react";
import { Outlet } from "react-router";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { setCurrentUser } from "@/features/session/sessionSlice";
import { useGetMeQuery } from "@/shared/api/api";

export function RequireSession() {
  const accessToken = useAppSelector((state) => state.session.accessToken);
  const dispatch = useAppDispatch();
  const { data: me, isLoading, isError } = useGetMeQuery(undefined, { skip: !accessToken });

  useEffect(() => {
    if (me) {
      dispatch(setCurrentUser(me));
    }
  }, [me, dispatch]);

  if (!accessToken || isLoading) {
    return <p>Loading session…</p>;
  }

  if (isError || !me) {
    return <p>Could not load session.</p>;
  }

  return <Outlet />;
}
