import { useState } from "react";
import { Link, useParams } from "react-router";
import { ConflictNotice, DetailGrid, DetailItem, Muted, Page, PageHeading, StatusBadge } from "@/components/portal";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button, buttonVariants } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import {
  useDisableAdviserMutation,
  useEnableAdviserMutation,
  useGetAdviserQuery,
} from "@/features/advisers/advisersApi";
import { cn } from "cn";

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
    return <Muted>Not found</Muted>;
  }

  if (isLoading || !data) {
    return <Muted>Loading…</Muted>;
  }

  return (
    <Page className="max-w-lg">
      <PageHeading
        title={data.name}
        actions={
          <>
            <Link to={`/advisers/${data.id}/edit`} className={cn(buttonVariants({ variant: "outline", size: "sm" }))}>
              Edit
            </Link>
            {data.status === "disabled" ? (
              <Button type="button" variant="outline" size="sm" onClick={() => void runStatusChange("enable")}>
                Enable
              </Button>
            ) : (
              <Button type="button" variant="destructive" size="sm" onClick={() => void runStatusChange("disable")}>
                Disable
              </Button>
            )}
          </>
        }
      />
      {conflict ? <ConflictNotice /> : null}
      {guardMessage ? (
        <Alert variant="destructive">
          <AlertDescription>
            {guardMessage}{" "}
            <Link to={`/customers?adviserId=${data.id}`} className="underline underline-offset-4">
              View assigned customers
            </Link>
          </AlertDescription>
        </Alert>
      ) : null}
      <Card>
        <CardContent>
          <DetailGrid>
            <DetailItem label="Email">{data.email}</DetailItem>
            <DetailItem label="Status">
              <StatusBadge status={data.status} />
            </DetailItem>
            <DetailItem label="Created">{data.created.slice(0, 10)}</DetailItem>
            <DetailItem label="Id">{data.id}</DetailItem>
          </DetailGrid>
        </CardContent>
      </Card>
    </Page>
  );
}
