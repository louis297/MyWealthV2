import { useState } from "react";
import { Link, useParams } from "react-router";
import {
  useDisableAdviserMutation,
  useEnableAdviserMutation,
  useGetAdviserQuery,
} from "@/features/advisers/advisersApi";

const assignedCustomersMessage =
  "Reassign or disable assigned customers before disabling this adviser.";

function validationMessages(error: unknown): string[] {
  if (typeof error !== "object" || error === null || !("data" in error)) {
    return [];
  }
  const data = (error as { data?: { errors?: Record<string, string[]> } }).data;
  if (!data?.errors) {
    return [];
  }
  return Object.values(data.errors).flat();
}

function isConflict(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 409;
}

export function AdviserDetailPage() {
  const { id } = useParams();
  const { data, error, isLoading, refetch } = useGetAdviserQuery(id ?? "", { skip: !id });
  const [disableAdviser] = useDisableAdviserMutation();
  const [enableAdviser] = useEnableAdviserMutation();
  const [conflict, setConflict] = useState(false);
  const [guardMessage, setGuardMessage] = useState<string | null>(null);

  async function runStatusChange(action: "disable" | "enable") {
    if (!data) {
      return;
    }
    if (action === "disable" && !window.confirm("Disable this adviser?")) {
      return;
    }

    try {
      const body = { id: data.id, rowVersion: data.rowVersion };
      if (action === "disable") {
        await disableAdviser(body).unwrap();
      } else {
        await enableAdviser(body).unwrap();
      }
      setConflict(false);
      setGuardMessage(null);
    } catch (reason: unknown) {
      if (isConflict(reason)) {
        setConflict(true);
        void refetch();
        return;
      }
      const messages = validationMessages(reason);
      if (messages.includes(assignedCustomersMessage)) {
        setGuardMessage(assignedCustomersMessage);
      }
    }
  }

  if (error && "status" in error && error.status === 404) {
    return <p>Not found</p>;
  }

  if (isLoading || !data) {
    return <p>Loading…</p>;
  }

  return (
    <div className="max-w-lg space-y-4">
      <h1 className="text-xl font-medium">{data.name}</h1>
      {conflict ? <p>This row changed. Reload and try again.</p> : null}
      {guardMessage ? (
        <p>
          {guardMessage}{" "}
          <Link to={`/customers?adviserId=${data.id}`}>View assigned customers</Link>
        </p>
      ) : null}
      <dl className="space-y-2 text-sm">
        <div>
          <dt className="text-slate-500">Email</dt>
          <dd>{data.email}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Status</dt>
          <dd>{data.status}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Created</dt>
          <dd>{data.created.slice(0, 10)}</dd>
        </div>
        <div>
          <dt className="text-slate-500">Id</dt>
          <dd>{data.id}</dd>
        </div>
      </dl>
      <div className="flex gap-2 text-sm">
        <Link to={`/advisers/${data.id}/edit`} className="border border-slate-300 bg-white px-3 py-1">
          Edit
        </Link>
        {data.status === "disabled" ? (
          <button type="button" onClick={() => void runStatusChange("enable")}>
            Enable
          </button>
        ) : (
          <button type="button" onClick={() => void runStatusChange("disable")}>
            Disable
          </button>
        )}
      </div>
    </div>
  );
}
