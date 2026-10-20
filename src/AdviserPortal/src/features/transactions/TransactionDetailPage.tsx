import { useParams } from "react-router";
import { Muted, Page, PageHeading } from "@/components/portal";
import { Badge } from "@/components/ui/badge";
import { Card, CardContent } from "@/components/ui/card";
import {
  canReverse,
  ReverseTransactionButton,
} from "@/features/transactions/ReverseTransactionButton";
import { useGetTransactionQuery } from "@/features/transactions/transactionsApi";

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

export function TransactionDetailPage() {
  const { id } = useParams();
  const { data, error } = useGetTransactionQuery(id ?? "", { skip: !id });

  return (
    <Page className="max-w-lg">
      <PageHeading title="Transaction" />
      {isNotFound(error) ? <Muted>Not found</Muted> : null}
      {!data && !isNotFound(error) ? <Muted>Loading…</Muted> : null}
      {data ? (
        <Card>
          <CardContent className="flex flex-col items-start gap-3">
            <p className="font-mono text-xs break-all">{data.id}</p>
            <Badge variant="secondary">{data.type}</Badge>
            {canReverse(data) ? <ReverseTransactionButton id={data.id} /> : null}
          </CardContent>
        </Card>
      ) : null}
    </Page>
  );
}
