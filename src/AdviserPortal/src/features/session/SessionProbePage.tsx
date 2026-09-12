import { useEffect } from "react";
import { useAppDispatch } from "@/app/hooks";
import { setCurrentUser } from "@/features/session/sessionSlice";
import { useGetMeQuery } from "@/shared/api/api";

export function SessionProbePage() {
  const dispatch = useAppDispatch();
  const { data, error, isLoading } = useGetMeQuery();

  useEffect(() => {
    if (data) {
      dispatch(setCurrentUser(data));
    }
  }, [data, dispatch]);

  if (isLoading) {
    return <p>Loading session…</p>;
  }

  if (error || !data) {
    return <p>Could not load session.</p>;
  }

  return (
    <div>
      <p>
        {data.name} · {data.role} · {data.tenantCode ?? "no tenant"}
      </p>
      <pre>{JSON.stringify(data, null, 2)}</pre>
    </div>
  );
}
