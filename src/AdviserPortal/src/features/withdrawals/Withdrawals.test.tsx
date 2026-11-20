import { fireEvent, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { WithdrawalDetailPage } from "@/features/withdrawals/WithdrawalDetailPage";
import { WithdrawalsListPage } from "@/features/withdrawals/WithdrawalsListPage";
import { WithdrawPage } from "@/features/withdrawals/WithdrawPage";
import { ShellLayout } from "@/layouts/ShellLayout";
import { chooseOption } from "@/test/chooseOption";
import { adviser, feeUser, json, problem, renderFees, requestsTo, stubBrowser } from "@/test/fees";

const accountId = "aaaaaaaa-0000-0000-0000-000000000001";
const customerId = "cccccccc-0000-0000-0000-000000000001";
const withdrawalId = "77777777-0000-0000-0000-000000000001";
const payeeId = "bbbbbbbb-0000-0000-0000-000000000001";
const me = feeUser();

const account = {
  id: accountId,
  tenantId: "22222222-2222-2222-2222-222222222222",
  customerId,
  name: "Managed portfolio",
  type: "Brokerage",
  custodyMode: "Custodial",
  currency: "USD",
  depositReference: "K7M3Q9PXB",
  cashBalance: 1000,
  reservedCash: 300,
  availableCash: 700,
  status: "Open",
  isActive: true,
  rowVersion: "AAAA",
};

const payee = {
  id: payeeId,
  customerId,
  currency: "USD",
  bankName: "ANZ",
  accountName: "J Smith",
  accountNumber: "06-0123-0456789-00",
  status: "Verified",
  added: { by: { id: "x", name: "Ann Adviser" }, at: "2026-11-20T03:00:00Z" },
  review: null,
  removal: null,
  rowVersion: "AAAA",
};

function withdrawal(overrides: Record<string, unknown> = {}) {
  return {
    id: withdrawalId,
    accountId,
    customerId,
    nominatedBankAccount: { id: payeeId, bankName: "ANZ", accountName: "J Smith", accountNumber: "****8900" },
    amount: 300,
    currency: "USD",
    reference: "K7M3Q9PXB",
    memo: "By phone",
    status: "Requested",
    requestedBy: { id: "eeeeeeee-0000-0000-0000-000000000001", name: "Ann Adviser" },
    requestedAt: "2026-11-20T04:00:00Z",
    decision: null,
    payment: null,
    payoutTransactionId: null,
    returnTransactionId: null,
    failureReason: null,
    returnReason: null,
    rowVersion: "AAAA",
    ...overrides,
  };
}

const page = (items: unknown[]) => json({ items, page: 1, pageSize: 20, totalCount: items.length });

async function body(request: Request) {
  return JSON.parse(await request.clone().text());
}

describe("Withdrawals navigation (withdrawals D46)", () => {
  beforeEach(() => stubBrowser("/dashboard"));

  it.each([["a TenantAdmin", feeUser()], ["an Adviser", adviser()]])("shows Withdrawals to %s", async (_, user) => {
    renderFees(user, [{ path: "/dashboard", element: <ShellLayout /> }], "/dashboard");
    expect(await screen.findByRole("link", { name: /^Withdrawals/ })).toHaveAttribute("href", "/withdrawals");
  });
});

describe("WithdrawPage (withdrawals D45)", () => {
  const routes = [
    { path: "/accounts/:id/withdraw", element: <WithdrawPage /> },
    { path: "/withdrawals/:id", element: <p>Withdrawal page</p> },
  ];

  beforeEach(() => stubBrowser(`/accounts/${accountId}/withdraw`));

  function serve(payees: unknown[], post?: () => Response) {
    return (request: Request) => {
      if (request.method === "POST") {
        return post?.() ?? json({ id: withdrawalId }, 201);
      }
      if (request.url.includes(`/accounts/${accountId}`)) {
        return json(account);
      }
      if (request.url.includes("/nominated-bank-accounts")) {
        return page(payees);
      }
      return undefined;
    };
  }

  it("asks for the Customer's Verified accounts in the currency, reviews and posts with one key", async () => {
    const { requests } = renderFees(me, routes, `/accounts/${accountId}/withdraw`, serve([payee]));

    expect(await screen.findByText("Available 700.00 USD")).toBeInTheDocument();
    const lookup = new URL(requestsTo(requests, "GET", "/nominated-bank-accounts")[0].url);
    expect(lookup.searchParams.get("customerId")).toBe(customerId);
    expect(lookup.searchParams.get("status")).toBe("Verified");
    expect(lookup.searchParams.get("currency")).toBe("USD");
    expect(screen.getByLabelText("Reference")).toHaveAttribute("placeholder", "K7M3Q9PXB");
    await chooseOption("To", "ANZ J Smith ****8900");
    fireEvent.change(screen.getByLabelText("Amount"), { target: { value: "300" } });
    fireEvent.change(screen.getByLabelText("Memo"), { target: { value: "By phone" } });
    fireEvent.click(screen.getByRole("button", { name: "Review" }));

    expect(await screen.findByText(
      "Withdraw 300.00 USD from Managed portfolio to ANZ J Smith ****8900. A firm administrator must approve it.",
    )).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Confirm withdrawal" }));

    expect(await screen.findByText("Withdrawal page")).toBeInTheDocument();
    const post = requestsTo(requests, "POST", "/withdrawals")[0];
    expect(post.headers.get("Idempotency-Key")).toMatch(/^[0-9a-f-]{36}$/i);
    expect(await body(post)).toEqual({
      accountId,
      nominatedBankAccountId: payeeId,
      amount: 300,
      reference: null,
      memo: "By phone",
    });
  });

  it("says how to add an account when the Customer has none in the currency", async () => {
    renderFees(me, routes, `/accounts/${accountId}/withdraw`, serve([]));

    expect(await screen.findByText("No verified bank account in USD. Add one on the customer page.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "customer page" })).toHaveAttribute("href", `/customers/${customerId}`);
  });

  it("shows a 400 at its field", async () => {
    renderFees(me, routes, `/accounts/${accountId}/withdraw`, serve([payee], () =>
      problem(400, { errors: { Amount: ["The account's available cash is 700.00 USD."] } })));

    await chooseOption("To", "ANZ J Smith ****8900");
    fireEvent.change(screen.getByLabelText("Amount"), { target: { value: "800" } });
    fireEvent.click(screen.getByRole("button", { name: "Review" }));
    fireEvent.click(await screen.findByRole("button", { name: "Confirm withdrawal" }));

    expect(await screen.findByText("The account's available cash is 700.00 USD.")).toBeInTheDocument();
  });
});

describe("WithdrawalsListPage (withdrawals D46)", () => {
  const routes = [{ path: "/withdrawals", element: <WithdrawalsListPage /> }];

  beforeEach(() => stubBrowser("/withdrawals"));

  it("lists Requested withdrawals by default with the columns and labels", async () => {
    const { requests } = renderFees(me, routes, "/withdrawals", (request) => {
      if (request.url.includes("/withdrawals")) {
        return page([withdrawal()]);
      }
      if (request.url.includes(`/accounts/${accountId}`)) {
        return json(account);
      }
      if (request.url.includes(`/users/customers/${customerId}`)) {
        return json({ id: customerId, name: "Ned North" });
      }
      return undefined;
    });

    expect(await screen.findByText("Waiting for approval")).toBeInTheDocument();
    expect(new URL(requestsTo(requests, "GET", "/withdrawals")[0].url).searchParams.get("status")).toBe("Requested");
    expect(screen.getByText("ANZ ****8900")).toBeInTheDocument();
    expect(await screen.findByText("Ned North")).toBeInTheDocument();
    expect(await screen.findByText("Managed portfolio")).toBeInTheDocument();
    expect(screen.getByText("Ann Adviser")).toBeInTheDocument();
  });

  it.each([
    ["Approved", "Approved, sending"],
    ["Sent", "Sent to bank"],
    ["Failed", "Bank refused"],
    ["Returned", "Returned by bank"],
  ])("labels %s as %s", async (status, label) => {
    renderFees(me, [{ path: "/withdrawals", element: <WithdrawalsListPage /> }], `/withdrawals?status=${status}`, (request) =>
      request.url.includes("/withdrawals") ? page([withdrawal({ status })]) : undefined);

    expect(await screen.findByText(label)).toBeInTheDocument();
  });
});

describe("WithdrawalDetailPage (withdrawals D47)", () => {
  const routes = [{ path: "/withdrawals/:id", element: <WithdrawalDetailPage /> }];

  beforeEach(() => stubBrowser(`/withdrawals/${withdrawalId}`));

  function serve(item: object, post?: () => Response) {
    return (request: Request) => {
      if (request.method === "POST") {
        return post?.() ?? new Response(null, { status: 204 });
      }
      if (request.url.includes(`/withdrawals/${withdrawalId}`)) {
        return json(item);
      }
      if (request.url.includes(`/accounts/${accountId}`)) {
        return json(account);
      }
      return undefined;
    };
  }

  it("lets a TenantAdmin who did not request it approve and reject, and anyone cancel", async () => {
    const { requests } = renderFees(me, routes, `/withdrawals/${withdrawalId}`, serve(withdrawal()));

    expect(await screen.findByText("Waiting for approval")).toBeInTheDocument();
    expect(screen.getByText("By phone")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reject" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Cancel withdrawal" })).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Approve" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/withdrawals/${withdrawalId}/approve`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/withdrawals/${withdrawalId}/approve`)[0])).toEqual({
      note: null,
      rowVersion: "AAAA",
    });
  });

  it("tells the requester to wait for another administrator", async () => {
    renderFees(me, routes, `/withdrawals/${withdrawalId}`, serve(withdrawal({ requestedBy: { id: me.id, name: me.name } })));

    expect(await screen.findByText("Waiting for another administrator to approve.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
  });

  it("offers an Adviser neither approve nor reject", async () => {
    renderFees(adviser(), routes, `/withdrawals/${withdrawalId}`, serve(withdrawal()));

    expect(await screen.findByRole("button", { name: "Cancel withdrawal" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Approve" })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Reject" })).not.toBeInTheDocument();
  });

  it("offers cancel on an Approved one only while no send was attempted", async () => {
    const pending = { status: "Pending", attempts: 0, lastAttemptAt: null, lastFailure: null, sentAt: null };
    renderFees(me, routes, `/withdrawals/${withdrawalId}`, serve(withdrawal({ status: "Approved", payment: pending })));
    expect(await screen.findByRole("button", { name: "Cancel withdrawal" })).toBeInTheDocument();
  });

  it("does not offer cancel after a send attempt, and shows the attempts and the last failure", async () => {
    const attempted = { status: "Pending", attempts: 2, lastAttemptAt: "2026-11-20T04:06:00Z", lastFailure: "Timeout", sentAt: null };
    renderFees(me, routes, `/withdrawals/${withdrawalId}`, serve(withdrawal({ status: "Approved", payment: attempted })));

    expect(await screen.findByText("Approved, sending")).toBeInTheDocument();
    expect(screen.getByText("2")).toBeInTheDocument();
    expect(screen.getByText("Timed out")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Cancel withdrawal" })).not.toBeInTheDocument();
  });

  it("links the payout and the return Reversal and shows the return reason", async () => {
    renderFees(me, routes, `/withdrawals/${withdrawalId}`, serve(withdrawal({
      status: "Returned",
      payoutTransactionId: "11111111-0000-0000-0000-000000000001",
      returnTransactionId: "11111111-0000-0000-0000-000000000002",
      returnReason: "Account closed",
    })));

    expect(await screen.findByRole("link", { name: "Payout" })).toHaveAttribute(
      "href", "/transactions/11111111-0000-0000-0000-000000000001");
    expect(screen.getByRole("link", { name: "Return" })).toHaveAttribute(
      "href", "/transactions/11111111-0000-0000-0000-000000000002");
    expect(screen.getByText("Account closed")).toBeInTheDocument();
  });

  it("reloads with the changed sentence on a 409", async () => {
    renderFees(me, routes, `/withdrawals/${withdrawalId}`, serve(withdrawal(), () => problem(409, { code: "stale_row_version" })));

    fireEvent.click(await screen.findByRole("button", { name: "Approve" }));

    expect(await screen.findByText("This withdrawal changed. Check it and try again.")).toBeInTheDocument();
  });

  it("shows the sentence of a 400", async () => {
    renderFees(me, routes, `/withdrawals/${withdrawalId}`, serve(withdrawal(), () =>
      problem(400, { errors: { NominatedBankAccountId: ["The bank account is no longer verified."] } })));

    fireEvent.click(await screen.findByRole("button", { name: "Approve" }));

    expect(await screen.findByText("The bank account is no longer verified.")).toBeInTheDocument();
  });
});
