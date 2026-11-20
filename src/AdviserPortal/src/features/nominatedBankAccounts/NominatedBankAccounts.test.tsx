import { fireEvent, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import { AddBankAccountPage } from "@/features/nominatedBankAccounts/AddBankAccountPage";
import { BankAccountChecksPage } from "@/features/nominatedBankAccounts/BankAccountChecksPage";
import { CustomerBankAccountsSection } from "@/features/nominatedBankAccounts/CustomerBankAccountsSection";
import { ShellLayout } from "@/layouts/ShellLayout";
import { chooseOption } from "@/test/chooseOption";
import { adviser, feeUser, json, problem, renderFees, requestsTo, stubBrowser } from "@/test/fees";

const customerId = "cccccccc-0000-0000-0000-000000000001";
const me = feeUser();

function row(overrides: Record<string, unknown> = {}) {
  return {
    id: "aaaaaaaa-0000-0000-0000-00000000000" + String(overrides.n ?? 1),
    customerId,
    currency: "USD",
    bankName: "ANZ",
    accountName: "J Smith",
    accountNumber: "06-0123-0456789-00",
    status: "Pending",
    added: { by: { id: "eeeeeeee-0000-0000-0000-000000000001", name: "Ann Adviser" }, at: "2026-11-20T03:00:00Z" },
    review: null,
    removal: null,
    rowVersion: "AAAA",
    ...overrides,
  };
}

const page = (items: unknown[]) => json({ items, page: 1, pageSize: 100, totalCount: items.length });

async function body(request: Request) {
  return JSON.parse(await request.clone().text());
}

describe("Bank account checks navigation (withdrawals D42)", () => {
  beforeEach(() => stubBrowser("/dashboard"));

  it("shows Bank account checks to a TenantAdmin only", async () => {
    const routes = [{ path: "/dashboard", element: <ShellLayout /> }];
    renderFees(feeUser(), routes, "/dashboard");
    expect(await screen.findByRole("link", { name: /Bank account checks/ })).toHaveAttribute("href", "/nominated-bank-accounts");
  });

  it("hides Bank account checks from an Adviser", async () => {
    const routes = [{ path: "/dashboard", element: <ShellLayout /> }];
    renderFees(adviser(), routes, "/dashboard");
    expect(await screen.findByRole("link", { name: /Accounts/ })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /Bank account checks/ })).not.toBeInTheDocument();
  });
});

describe("CustomerBankAccountsSection (withdrawals D40, D43)", () => {
  const routes = [{ path: "/customers/:id", element: <CustomerBankAccountsSection customerId={customerId} /> }];

  beforeEach(() => stubBrowser(`/customers/${customerId}`));

  it("lists the open rows with their labels, hides removed ones until asked, and links Add bank account", async () => {
    renderFees(me, routes, `/customers/${customerId}`, (request) =>
      request.url.includes("/nominated-bank-accounts")
        ? page([
            row({ n: 1 }),
            row({ n: 2, status: "Verified", accountNumber: "222" }),
            row({ n: 3, status: "Removed", accountNumber: "333" }),
          ])
        : undefined);

    expect(await screen.findByText("Waiting for check")).toBeInTheDocument();
    expect(screen.getByText("Verified")).toBeInTheDocument();
    expect(screen.getByText("06-0123-0456789-00")).toBeInTheDocument();
    expect(screen.getAllByText("Ann Adviser").length).toBeGreaterThan(0);
    expect(screen.queryByText("333")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Add bank account" })).toHaveAttribute("href", `/customers/${customerId}/bank-accounts/new`);
    expect(screen.getAllByRole("button", { name: "Remove" })).toHaveLength(2);

    fireEvent.click(screen.getByLabelText("Show removed"));
    expect(await screen.findByText("333")).toBeInTheDocument();
  });

  it("removes a row with a required note in a dialog", async () => {
    const { requests } = renderFees(me, routes, `/customers/${customerId}`, (request) => {
      if (request.method === "POST") {
        return new Response(null, { status: 204 });
      }
      return request.url.includes("/nominated-bank-accounts") ? page([row()]) : undefined;
    });

    fireEvent.click(await screen.findByRole("button", { name: "Remove" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Withdrawals already approved to it are still paid.")).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirm" }));
    expect(await within(dialog).findByText("Enter a note.")).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText("Note"), { target: { value: "Closed" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Confirm" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/nominated-bank-accounts/${row().id}/remove`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/nominated-bank-accounts/${row().id}/remove`)[0])).toEqual({
      note: "Closed",
      rowVersion: "AAAA",
    });
  });
});

describe("AddBankAccountPage (withdrawals D41)", () => {
  const routes = [
    { path: "/customers/:id/bank-accounts/new", element: <AddBankAccountPage /> },
    { path: "/customers/:id", element: <p>Customer page</p> },
  ];

  beforeEach(() => stubBrowser(`/customers/${customerId}/bank-accounts/new`));

  async function fill() {
    fireEvent.change(await screen.findByLabelText("Bank"), { target: { value: "ANZ" } });
    fireEvent.change(screen.getByLabelText("Account name"), { target: { value: "J Smith" } });
    fireEvent.change(screen.getByLabelText("Account number"), { target: { value: "06-0123" } });
    await chooseOption("Currency", "USD US dollar");
  }

  it("says who the account must belong to, posts the fields and returns to the customer", async () => {
    const { requests } = renderFees(me, routes, `/customers/${customerId}/bank-accounts/new`, (request) =>
      request.method === "POST" ? json({ id: "new" }, 201) : undefined);

    expect(await screen.findByText(/Add only an account in the customer's own name/)).toBeInTheDocument();
    await fill();
    fireEvent.click(screen.getByRole("button", { name: "Add bank account" }));

    expect(await screen.findByText("Customer page")).toBeInTheDocument();
    expect(await body(requestsTo(requests, "POST", "/nominated-bank-accounts")[0])).toEqual({
      customerId,
      currency: "USD",
      bankName: "ANZ",
      accountName: "J Smith",
      accountNumber: "06-0123",
    });
  });

  it("shows a 400 at its field", async () => {
    renderFees(me, routes, `/customers/${customerId}/bank-accounts/new`, (request) =>
      request.method === "POST"
        ? problem(400, { errors: { AccountNumber: ["This bank account is already nominated for this customer."] } })
        : undefined);

    await fill();
    fireEvent.click(screen.getByRole("button", { name: "Add bank account" }));

    expect(await screen.findByText("This bank account is already nominated for this customer.")).toBeInTheDocument();
  });
});

describe("BankAccountChecksPage (withdrawals D42)", () => {
  const routes = [{ path: "/nominated-bank-accounts", element: <BankAccountChecksPage /> }];

  beforeEach(() => stubBrowser("/nominated-bank-accounts"));

  function serve(rows: unknown[], post?: () => Response) {
    return (request: Request) => {
      if (request.method === "POST") {
        return post?.() ?? new Response(null, { status: 204 });
      }
      if (request.url.includes("/nominated-bank-accounts")) {
        return page(rows);
      }
      if (request.url.includes(`/users/customers/${customerId}`)) {
        return json({ id: customerId, name: "Ned North" });
      }
      return undefined;
    };
  }

  it("lists Pending rows and verifies one with a required note", async () => {
    const { requests } = renderFees(me, routes, "/nominated-bank-accounts", serve([row()]));

    expect(await screen.findByText("Ned North")).toBeInTheDocument();
    expect(requestsTo(requests, "GET", "/nominated-bank-accounts")[0].url).toContain("status=Pending");
    fireEvent.click(screen.getByRole("button", { name: "Check" }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "Verify" }));
    expect(await within(dialog).findByText("Enter a note.")).toBeInTheDocument();
    fireEvent.change(within(dialog).getByLabelText("What did you check?"), { target: { value: "Statement sighted" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Verify" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/nominated-bank-accounts/${row().id}/verify`)).toHaveLength(1));
    expect(await body(requestsTo(requests, "POST", `/nominated-bank-accounts/${row().id}/verify`)[0])).toEqual({
      note: "Statement sighted",
      rowVersion: "AAAA",
    });
  });

  it("rejects with the reason", async () => {
    const { requests } = renderFees(me, routes, "/nominated-bank-accounts", serve([row()]));

    fireEvent.click(await screen.findByRole("button", { name: "Check" }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText("Why is it rejected?"), { target: { value: "Name differs" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Reject" }));

    await waitFor(() => expect(requestsTo(requests, "POST", `/nominated-bank-accounts/${row().id}/reject`)).toHaveLength(1));
  });

  it("disables both buttons on a row the TenantAdmin added", async () => {
    renderFees(me, routes, "/nominated-bank-accounts", serve([row({ added: { by: { id: me.id, name: me.name }, at: "2026-11-20T03:00:00Z" } })]));

    fireEvent.click(await screen.findByRole("button", { name: "Check" }));
    const dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByText("Added by you. Another administrator must check it.")).toBeInTheDocument();
    expect(within(dialog).getByRole("button", { name: "Verify" })).toBeDisabled();
    expect(within(dialog).getByRole("button", { name: "Reject" })).toBeDisabled();
  });

  it("shows the firm administrator sentence on a 403", async () => {
    renderFees(adviser(), routes, "/nominated-bank-accounts", () => problem(403, {}));

    expect(await screen.findByText("Only a firm administrator can check bank accounts.")).toBeInTheDocument();
  });

  it("reloads with the changed sentence on a 409", async () => {
    renderFees(me, routes, "/nominated-bank-accounts", serve([row()], () => problem(409, { code: "stale_row_version" })));

    fireEvent.click(await screen.findByRole("button", { name: "Check" }));
    const dialog = await screen.findByRole("dialog");
    fireEvent.change(within(dialog).getByLabelText("What did you check?"), { target: { value: "Statement" } });
    fireEvent.click(within(dialog).getByRole("button", { name: "Verify" }));

    expect(await screen.findByText("This bank account changed. Check it and try again.")).toBeInTheDocument();
  });
});
