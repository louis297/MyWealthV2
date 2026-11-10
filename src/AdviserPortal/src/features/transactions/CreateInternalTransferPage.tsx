import { useState } from "react";
import { useNavigate, useParams } from "react-router";
import { Messages, Muted, Page, PageHeading, Section, TextField } from "@/components/portal";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { type Account, useGetAccountQuery, useGetAccountsQuery } from "@/features/accounts/accountsApi";
import { useGetCustomerQuery, useGetCustomersQuery } from "@/features/customers/customersApi";
import { useCreateInternalTransferMutation } from "@/features/transactions/transactionsApi";
import { useDebouncedValue } from "@/shared/hooks/useDebouncedValue";
import { cn } from "cn";

type ChosenCustomer = { id: string; name: string };

/** Client-side eligibility of a destination Account (internal-transfers §9). */
function ineligibleReason(candidate: Account, source: Account): string | null {
  if (candidate.id === source.id) {
    return "Source account";
  }
  if (candidate.status !== "Open") {
    return "Closed";
  }
  if (candidate.currency !== source.currency) {
    return "Different currency";
  }
  return null;
}

function isNotFound(error: unknown) {
  return typeof error === "object" && error !== null && "status" in error && error.status === 404;
}

function problemMessages(error: unknown): string[] {
  if (isNotFound(error)) {
    return ["Not found"];
  }
  if (typeof error !== "object" || error === null || !("data" in error)) {
    return [];
  }
  const data = (error as { data?: { errors?: Record<string, string[]>; detail?: string } }).data;
  if (data?.errors) {
    return Object.values(data.errors).flat();
  }
  if (data?.detail) {
    return [data.detail];
  }
  return [];
}

export function CreateInternalTransferPage() {
  const { id: sourceId } = useParams();
  const navigate = useNavigate();
  const [idempotencyKey] = useState(() => crypto.randomUUID());
  const source = useGetAccountQuery(sourceId ?? "", { skip: !sourceId });
  const sourceCustomer = useGetCustomerQuery(source.data?.customerId ?? "", { skip: !source.data });
  const [chosenCustomer, setChosenCustomer] = useState<ChosenCustomer | null>(null);
  const [searchInput, setSearchInput] = useState("");
  const search = useDebouncedValue(searchInput.trim(), 300);
  const customers = useGetCustomersQuery({ search, enabledOnly: true }, { skip: search === "" });
  const destinationCustomerId = chosenCustomer?.id ?? source.data?.customerId;
  const destinationCustomerName = chosenCustomer?.name ?? sourceCustomer.data?.name;
  const accounts = useGetAccountsQuery(
    { customerId: destinationCustomerId, pageSize: 100 },
    { skip: !destinationCustomerId },
  );
  const [toAccountId, setToAccountId] = useState("");
  const [amount, setAmount] = useState("");
  const [bookedAt, setBookedAt] = useState("");
  const [memo, setMemo] = useState("");
  const [reference, setReference] = useState("");
  const [reviewing, setReviewing] = useState(false);
  const [messages, setMessages] = useState<string[]>([]);
  const [createInternalTransfer, { isLoading }] = useCreateInternalTransferMutation();

  if (source.isError) {
    return (
      <Page>
        <PageHeading title="Transfer" />
        <Messages messages={problemMessages(source.error)} />
      </Page>
    );
  }
  if (!source.data) {
    return (
      <Page>
        <PageHeading title="Transfer" />
        <Muted>Loading…</Muted>
      </Page>
    );
  }

  const from = source.data;
  const candidates = accounts.data?.items ?? [];
  const destination = candidates.find((item) => item.id === toAccountId && ineligibleReason(item, from) === null);
  const crossCustomer = destinationCustomerId !== from.customerId;
  const amountValue = Number(amount);

  function chooseCustomer(next: ChosenCustomer) {
    setChosenCustomer(next);
    setSearchInput("");
    setToAccountId("");
    setReviewing(false);
  }

  function onReview() {
    const problems: string[] = [];
    if (!destination) {
      problems.push("Choose a destination account.");
    }
    if (!(Number.isFinite(amountValue) && amountValue > 0)) {
      problems.push("Amount must be greater than 0.");
    }
    if (!bookedAt) {
      problems.push("Booked at is required.");
    }
    setMessages(problems);
    setReviewing(problems.length === 0);
  }

  async function onConfirm() {
    if (!destination) {
      return;
    }
    try {
      await createInternalTransfer({
        idempotencyKey,
        body: {
          fromAccountId: from.id,
          toAccountId: destination.id,
          amount: amountValue,
          bookedAt,
          memo: memo.trim() || null,
          reference: reference.trim() || null,
        },
      }).unwrap();
      navigate(`/accounts/${from.id}`);
    } catch (error: unknown) {
      setReviewing(false);
      const problems = problemMessages(error);
      setMessages(problems.length > 0 ? problems : ["Transfer failed. Check the details and try again."]);
    }
  }

  return (
    <Page>
      <PageHeading title="Transfer" description={`From ${from.name} (${sourceCustomer.data?.name ?? "…"})`} />
      <Messages messages={messages} />
      <Section title="Destination">
        <Card>
          <CardContent className="flex flex-col gap-4">
            <p className="text-sm">{`Destination customer: ${destinationCustomerName ?? "…"}`}</p>
            <TextField
              id="transfer-customer-search"
              label="Search customers"
              value={searchInput}
              onChange={(event) => setSearchInput(event.target.value)}
              autoComplete="off"
            />
            {search !== "" ? (
              <div className="flex flex-wrap gap-2" aria-label="Customer results">
                {(customers.data?.items ?? []).map((item) => (
                  <Button
                    key={item.id}
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={() => chooseCustomer({ id: item.id, name: item.name })}
                  >
                    {item.name}
                  </Button>
                ))}
                {customers.data && customers.data.items.length === 0 ? <Muted>No customers found.</Muted> : null}
              </div>
            ) : null}
            <div role="radiogroup" aria-label="Destination account" className="flex flex-col gap-2">
              {candidates.length === 0 ? <Muted>No accounts.</Muted> : null}
              {candidates.map((item) => {
                const reason = ineligibleReason(item, from);
                return (
                  <label
                    key={item.id}
                    className={cn("flex items-center gap-2 text-sm", reason ? "text-muted-foreground opacity-60" : null)}
                  >
                    <input
                      type="radio"
                      name="transfer-destination"
                      value={item.id}
                      disabled={reason !== null}
                      checked={toAccountId === item.id}
                      onChange={() => {
                        setToAccountId(item.id);
                        setReviewing(false);
                      }}
                    />
                    <span>{item.name}</span>
                    <span>{item.currency}</span>
                    {reason ? <span>({reason})</span> : null}
                  </label>
                );
              })}
            </div>
          </CardContent>
        </Card>
      </Section>
      <Section title="Details">
        <Card>
          <CardContent className="flex flex-col gap-4">
            <TextField
              id="transfer-amount"
              label="Amount"
              inputMode="decimal"
              value={amount}
              onChange={(event) => {
                setAmount(event.target.value);
                setReviewing(false);
              }}
            />
            <TextField
              id="transfer-booked-at"
              label="Booked at"
              type="datetime-local"
              value={bookedAt}
              onChange={(event) => {
                setBookedAt(event.target.value);
                setReviewing(false);
              }}
            />
            <TextField
              id="transfer-memo"
              label="Memo"
              value={memo}
              onChange={(event) => setMemo(event.target.value)}
              maxLength={200}
            />
            <TextField
              id="transfer-reference"
              label="Reference"
              value={reference}
              onChange={(event) => setReference(event.target.value)}
              maxLength={100}
            />
            {reviewing && destination ? (
              <div className="flex flex-col gap-3">
                <p className="text-sm font-medium">
                  {`Transfer ${amountValue.toFixed(2)} ${from.currency} from ${from.name} (${sourceCustomer.data?.name ?? "…"}) to ${destination.name} (${destinationCustomerName ?? "…"})`}
                </p>
                {crossCustomer ? (
                  <Alert>
                    <AlertDescription>
                      The two accounts belong to different customers. Check the destination before you confirm.
                    </AlertDescription>
                  </Alert>
                ) : null}
                <div className="flex gap-2">
                  <Button type="button" disabled={isLoading} onClick={() => void onConfirm()}>
                    Confirm transfer
                  </Button>
                  <Button type="button" variant="outline" onClick={() => setReviewing(false)}>
                    Edit
                  </Button>
                </div>
              </div>
            ) : (
              <Button type="button" onClick={onReview}>
                Review transfer
              </Button>
            )}
          </CardContent>
        </Card>
      </Section>
    </Page>
  );
}
