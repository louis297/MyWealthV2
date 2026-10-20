import { useEffect } from "react";
import { useAppDispatch } from "@/app/hooks";
import { Muted } from "@/components/portal";
import { Card, CardContent } from "@/components/ui/card";
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
    return <Muted>Loading session…</Muted>;
  }

  if (error || !data) {
    return <Muted>Could not load session.</Muted>;
  }

  return (
    <Card>
      <CardContent className="flex flex-col gap-3">
        <p className="text-sm">
          {data.name} · {data.role} · {data.tenantCode ?? "no tenant"}
        </p>
        <pre className="overflow-auto rounded-md bg-muted p-3 text-xs">{JSON.stringify(data, null, 2)}</pre>
      </CardContent>
    </Card>
  );
}
