import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

export type CustodyMode = "External" | "Custodial";

/** account-closure R27: the Account's latest account closure on the account item. */
export type AccountClosureSummary = {
  id: string;
  status: string;
  payoutAmount: number;
  withdrawalId: string | null;
  requestedAt: string;
  completedAt: string | null;
};

export type Account = {
  id: string;
  tenantId: string;
  customerId: string;
  name: string;
  type: string;
  custodyMode: string;
  currency: string;
  // client-money-accounts R14: set on a Custodial account, null on an External one.
  depositReference?: string | null;
  cashBalance: number;
  /** withdrawals R35: the sum of the open withdrawals; 0 on an External account. */
  reservedCash?: number;
  /** withdrawals R35: cash balance minus reserved cash. */
  availableCash?: number;
  status: string;
  isActive: boolean;
  rowVersion: string;
  /** account-closure R27: null when the Account has no closure. */
  closure?: AccountClosureSummary | null;
  /** interest-run R35: unposted daily accruals, 8 decimals; 0 on an External account. */
  accruedInterest?: number;
  /** interest-run R35: the last UTC day accrued through, or null. */
  interestAccruedThrough?: string | null;
};

export type AccountListArgs = {
  page?: number;
  pageSize?: number;
  customerId?: string;
  type?: string;
  custodyMode?: CustodyMode;
  /** deposit-matching R37: at most one Account, matched exactly. */
  depositReference?: string;
};

export type CreateAccountBody = {
  customerId: string;
  name: string;
  type: string;
  custodyMode: CustodyMode;
  currency: string;
};

// client-money-accounts R22: where to pay in to an Open Custodial account.
export type DepositInstructions = {
  accountId: string;
  currency: string;
  reference: string;
  bankName: string;
  accountName: string;
  accountNumber: string;
};

export type AccountHolding = {
  id: string;
  accountId: string;
  instrumentId: string;
  symbol: string;
  name: string;
  quantity: number;
  cost: number;
  costCurrency: string;
};

export const accountsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getAccounts: builder.query<PagedList<Account>, AccountListArgs | void>({
      query: (args) => {
        const params = new URLSearchParams();
        params.set("page", String(args?.page ?? 1));
        params.set("pageSize", String(args?.pageSize ?? 20));
        if (args?.customerId) {
          params.set("customerId", args.customerId);
        }
        if (args?.type) {
          params.set("type", args.type);
        }
        if (args?.custodyMode) {
          params.set("custodyMode", args.custodyMode);
        }
        if (args?.depositReference) {
          params.set("depositReference", args.depositReference);
        }
        return `/accounts?${params.toString()}`;
      },
      providesTags: ["Account"],
    }),
    getAccount: builder.query<Account, string>({
      query: (id) => `/accounts/${id}`,
      providesTags: (_result, _error, id) => [{ type: "Account", id }],
    }),
    getAccountHoldings: builder.query<{ items: AccountHolding[] }, string>({
      query: (id) => `/accounts/${id}/holdings`,
      providesTags: (_result, _error, id) => [{ type: "Account", id }],
    }),
    getDepositInstructions: builder.query<DepositInstructions, string>({
      query: (id) => `/accounts/${id}/deposit-instructions`,
      providesTags: (_result, _error, id) => [{ type: "Account", id }],
    }),
    createAccount: builder.mutation<{ id: string }, CreateAccountBody>({
      query: (body) => ({
        url: "/accounts",
        method: "POST",
        body,
      }),
      invalidatesTags: ["Account"],
    }),
    closeAccount: builder.mutation<void, { id: string; rowVersion: string }>({
      query: ({ id, rowVersion }) => ({
        url: `/accounts/${id}/close`,
        method: "POST",
        body: { rowVersion },
      }),
      invalidatesTags: (_result, _error, arg) => [{ type: "Account", id: arg.id }, "Account"],
    }),
    reopenAccount: builder.mutation<void, { id: string; rowVersion: string }>({
      query: ({ id, rowVersion }) => ({
        url: `/accounts/${id}/reopen`,
        method: "POST",
        body: { rowVersion },
      }),
      invalidatesTags: (_result, _error, arg) => [{ type: "Account", id: arg.id }, "Account"],
    }),
  }),
});

export const {
  useGetAccountsQuery,
  useGetAccountQuery,
  useGetAccountHoldingsQuery,
  useGetDepositInstructionsQuery,
  useCreateAccountMutation,
  useCloseAccountMutation,
  useReopenAccountMutation,
} = accountsApi;
