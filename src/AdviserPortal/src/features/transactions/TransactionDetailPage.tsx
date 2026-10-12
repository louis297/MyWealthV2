import { useParams } from "react-router";
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
    <div className="max-w-lg space-y-4">
      <h1 className="text-xl font-medium">Transaction</h1>
      {isNotFound(error) ? <p>Not found</p> : null}
      {!data && !isNotFound(error) ? <p>Loading…</p> : null}
      {data ? (
        <>
          <p>{data.id}</p>
          <p>{data.type}</p>
          {canReverse(data) ? <ReverseTransactionButton id={data.id} /> : null}
        </>
      ) : null}
    </div>
  );
}
