import { type FormEvent, useEffect, useState } from "react";
import { useNavigate, useParams } from "react-router";
import { ConflictNotice, Muted, Page, PageHeading, TextField } from "@/components/portal";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
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
    return <Muted>Not found</Muted>;
  }

  if (isLoading || !data) {
    return <Muted>Loading…</Muted>;
  }

  return (
    <Page className="max-w-lg">
      <PageHeading title="Edit adviser" />
      {conflict ? <ConflictNotice /> : null}
      <Card>
        <CardContent>
          <form className="flex flex-col gap-4" onSubmit={(event) => void onSubmit(event)}>
            <TextField
              id="edit-adviser-name"
              label="Name"
              value={name}
              onChange={(event) => setName(event.target.value)}
              required
            />
            <Button type="submit">Save</Button>
          </form>
        </CardContent>
      </Card>
    </Page>
  );
}
