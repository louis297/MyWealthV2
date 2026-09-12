import { useEffect } from "react";
import { useAppSelector } from "@/app/hooks";
import { startAuthorize } from "@/features/session/oidc";

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

  return <p>Signed in</p>;
}
