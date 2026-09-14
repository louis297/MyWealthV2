import { useEffect, useState } from "react";
import { useNavigate } from "react-router";
import { completeCallback } from "@/features/session/oidc";

export function CallbackPage() {
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void completeCallback()
      .then((path) => navigate(path, { replace: true }))
      .catch((reason: unknown) => {
        setError(reason instanceof Error ? reason.message : "Sign-in failed.");
      });
  }, [navigate]);

  if (error) {
    return <p>{error}</p>;
  }

  return <p>Completing sign-in…</p>;
}
