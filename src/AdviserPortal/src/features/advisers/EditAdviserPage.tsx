import { type FormEvent, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { useGetAdviserQuery, useUpdateAdviserMutation } from "@/features/advisers/advisersApi";

function isConflict(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 409;
}

export function EditAdviserPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { data, error, isLoading, refetch } = useGetAdviserQuery(id ?? "", { skip: !id });
  const [updateAdviser] = useUpdateAdviserMutation();
  const [name, setName] = useState("");
  const [conflict, setConflict] = useState(false);

  useEffect(() => {
    if (data) {
      setName(data.name);
    }
  }, [data]);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (!data) {
      return;
    }

    try {
      await updateAdviser({ id: data.id, name: name.trim(), rowVersion: data.rowVersion }).unwrap();
      void navigate(`/advisers/${data.id}`);
    } catch (reason: unknown) {
      if (isConflict(reason)) {
        setConflict(true);
        void refetch();
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
      <h1 className="text-xl font-medium">Edit adviser</h1>
      {conflict ? <p>This row changed. Reload and try again.</p> : null}
      <form className="space-y-3" onSubmit={(event) => void onSubmit(event)}>
        <div>
          <label htmlFor="edit-adviser-name" className="block text-sm">
            Name
          </label>
          <input
            id="edit-adviser-name"
            className="mt-1 w-full border border-slate-300 px-2 py-1"
            value={name}
            onChange={(event) => setName(event.target.value)}
            required
          />
        </div>
        <button type="submit" className="border border-slate-300 bg-white px-3 py-1 text-sm">
          Save
        </button>
      </form>
    </div>
  );
}
