import { useEffect } from "react";
import { useAppSelector } from "@/app/hooks";
import { startAuthorize } from "@/features/session/oidc";
import { SessionProbePage } from "@/features/session/SessionProbePage";

export function HomePage() {
  const accessToken = useAppSelector((state) => state.session.accessToken);

  useEffect(() => {
    if (!accessToken) {
      void startAuthorize();
    }
  }, [accessToken]);

  if (!accessToken) {
    return <p>Redirecting to sign in…</p>;
  }

  return <SessionProbePage />;
}
