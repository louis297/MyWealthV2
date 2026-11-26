import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

export type Account = {
  id: string;
  name: string;
  type: string;
  custodyMode: string;
  currency: string;
  cashBalance: number;
  status: string;
  /** client-money-accounts R14: set on a Custodial account, null on an External one. */
  depositReference?: string | null;
  /** interest-run R35: unposted daily accruals, 8 decimals; 0 on an External account. */
  accruedInterest?: number;
  interestAccruedThrough?: string | null;
  /** dividends R17: whether dividends with a reinvestment plan are paid in shares; false on an External account. */
  reinvestDividends?: boolean;
  /** valuation-run R20: the last day stored for a Custodial account; null before the first and on an External one. */
  valuedThrough?: string | null;
  /** account-closure R27: the Account's latest account closure, or null. */
  closure?: {
    id: string;
    status: string;
    payoutAmount: number;
    withdrawalId: string | null;
    requestedAt: string;
    completedAt: string | null;
  } | null;
};

/** valuation-run R17: one stored valuation of an Account, in its currency. */
export type StoredValuation = {
  date: string;
  currency: string;
  cash: number;
  holdingsValue: number;
  total: number;
  complete: boolean;
  unvaluedCount: number;
};

/** client-money-accounts R22: where to pay in to an Open Custodial account. */
export type DepositInstructions = {
  accountId: string;
  currency: string;
  reference: string;
  bankName: string;
  accountName: string;
  accountNumber: string;
};

export type Holding = {
  id: string;
  instrumentId?: string;
  symbol: string;
  name: string;
  quantity: number;
  /** orders R41, in-kind-transfers R43: the quantity live sell orders and transfers out reserve. */
  reservedQuantity?: number;
  /** in-kind-transfers R43: bought on the platform and not settled yet. */
  unsettledQuantity?: number;
};

/** orders R15: an order as the Customer reads it; no memo, no cancel note and no send attempts. */
export type CustomerOrder = {
  id: string;
  instrument: { id: string; symbol: string; name: string };
  side: string;
  quantity: number;
  filledQuantity: number;
  status: string;
  placedAt: string;
  closedAt: string | null;
  /** rebalances R27: the rebalance that placed the order; null on a person's order. */
  rebalanceId?: string | null;
};

export type ActivityCash = {
  accountId: string;
  amount: number;
  currency: string;
  /** `Principal`, `Fee` or `Tax` (interest-run R34). */
  role?: string;
  /** interest-run R34: the tax detail of a `Tax` leg. */
  tax?: { kind: string; ratePercent: number; basisAmount: number; amount: number; currency: string } | null;
};

export type ActivityFx = {
  baseCurrency: string;
  quoteCurrency: string;
  rate: number;
  rateAsOfDate: string;
};

export type Activity = {
  id: string;
  type: string;
  bookedAt: string;
  memo: string | null;
  /** Legs on the caller's own accounts only. */
  cash: ActivityCash[];
  /** The rate used by an FX conversion (or its reversal); otherwise null. */
  fx?: ActivityFx | null;
  /** deposit-matching R36: the bank's details on a deposit and its Reversal; otherwise null. */
  bankReceipt?: ActivityBankReceipt | null;
  /** transaction-source R10: the process that wrote the transaction. */
  source?: { kind: string; id: string | null } | null;
  /** withdrawals R41: the bank's details on a payout and its Reversal; otherwise null. */
  payment?: { valueDate: string; reference: string | null; payeeName: string | null; payeeAccount: string | null } | null;
  /** interest-run R34: the period an interest posting pays; otherwise null. */
  interest?: { periodFrom: string; periodTo: string; days: number; accruedAmount: number; grossAmount: number } | null;
  /** The security legs on the caller's own accounts. */
  security?: { accountId: string; instrumentId: string; quantity: number }[];
  /** orders R38: the broker's execution behind an order fill; otherwise null. */
  execution?: {
    orderId: string;
    price: number;
    tradeDate: string;
    settlementDate: string;
    brokerReference: string | null;
    settledAt: string | null;
    /** trade-settlement R34: when the broker took the fill back. */
    brokenAt?: string | null;
  } | null;
  /** migration-batches R31: where a migrated opening balance came from; otherwise null. */
  migration?: { asOfDate: string; previousProvider: string } | null;
  /** in-kind-transfers R38: the transfer behind a StockIn or StockOut; otherwise null. */
  inKindTransfer?: { transferId: string; direction: string; counterpartyName: string } | null;
  /** dividends R39: the dividend a Dividend pays; otherwise null. */
  dividend?: {
    corporateActionId: string;
    instrument: { id: string; symbol: string; name: string };
    quantity: number;
    amountPerShare: number;
    currency: string;
    imputationCredit: number;
    reinvestedQuantity: number | null;
    reinvestmentPrice: number | null;
    exDate: string;
    paymentDate: string | null;
  } | null;
  /** corporate-actions R37: the event behind an entitlement posting; otherwise null. */
  corporateAction?: {
    id: string;
    kind: string;
    ratioNew: number;
    ratioHeld: number;
    exDate: string;
    recordDate: string | null;
    paymentDate: string | null;
    quantityBefore: number;
    quantityAfter: number;
  } | null;
};

/** withdrawals R25: a withdrawal as the Customer reads it; the payee number is masked, no memo, no note. */
/** in-kind-transfers R28: a transfer as the Customer reads it; no memo, no send attempts and no decision note. */
export type CustomerInKindTransfer = {
  id: string;
  accountId: string;
  direction: string;
  instrument: { id: string; symbol: string; name: string };
  quantity: number;
  counterparty: { name: string };
  status: string;
  requestedAt: string;
  decision: { at: string } | null;
  setAside: { at: string } | null;
  completedAt: string | null;
};

export type CustomerWithdrawal = {
  id: string;
  amount: number;
  currency: string;
  status: string;
  requestedAt: string;
  nominatedBankAccount: { bankName: string; accountNumber: string };
};

export type ActivityBankReceipt = {
  valueDate: string;
  reference: string | null;
  payerName: string | null;
  payerAccount: string | null;
};

export const accountsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    /** One custody group of the caller's accounts (customer-portal C16). */
    getAccounts: builder.query<PagedList<Account>, string>({
      query: (custodyMode) => `/accounts?custodyMode=${encodeURIComponent(custodyMode)}&page=1&pageSize=100`,
      providesTags: ["Account"],
    }),
    getAccount: builder.query<Account, string>({
      query: (id) => `/accounts/${id}`,
      providesTags: ["Account"],
    }),
    /** C47: the stored valuations of a range, newest first, at most 100 days a page. */
    getAccountValuations: builder.query<PagedList<StoredValuation>, { id: string; from: string; to: string }>({
      query: ({ id, from, to }) => `/accounts/${id}/valuations?from=${from}&to=${to}&pageSize=100`,
      providesTags: ["Account"],
    }),
    getCashBalance: builder.query<
      {
        cashBalance: number;
        currency: string;
        reservedCash?: number;
        availableCash?: number;
        /** orders R44: the net cash of sales that have not settled. */
        unsettledCash?: number;
        /** orders R44: available cash minus unsettled cash. */
        withdrawableCash?: number;
      },
      string
    >({
      query: (id) => `/accounts/${id}/cash-balance`,
    }),
    /** C32: the Customer's orders on one account, newest first. */
    getAccountOrders: builder.query<PagedList<CustomerOrder>, string>({
      query: (accountId) => `/orders?accountId=${encodeURIComponent(accountId)}&page=1&pageSize=100`,
      providesTags: ["Order"],
    }),
    /** C24: the Customer's withdrawals from one account. */
    getAccountWithdrawals: builder.query<PagedList<CustomerWithdrawal>, string>({
      query: (accountId) => `/withdrawals?accountId=${encodeURIComponent(accountId)}&page=1&pageSize=100`,
      providesTags: ["Withdrawal"],
    }),
    getAccountInKindTransfers: builder.query<PagedList<CustomerInKindTransfer>, string>({
      query: (accountId) => `/in-kind-transfers?accountId=${encodeURIComponent(accountId)}&page=1&pageSize=100`,
      providesTags: ["InKindTransfer"],
    }),
    getDepositInstructions: builder.query<DepositInstructions, string>({
      query: (id) => `/accounts/${id}/deposit-instructions`,
      providesTags: ["Account"],
    }),
    getHoldings: builder.query<{ items: Holding[] }, string>({
      query: (id) => `/accounts/${id}/holdings`,
    }),
    getActivity: builder.query<PagedList<Activity>, string>({
      query: (id) => `/transactions?accountId=${encodeURIComponent(id)}&page=1&pageSize=20`,
      providesTags: ["Transaction"],
    }),
  }),
});

export const {
  useGetAccountOrdersQuery,
  useGetAccountWithdrawalsQuery,
  useGetAccountInKindTransfersQuery,
  useGetAccountsQuery,
  useGetAccountQuery,
  useGetAccountValuationsQuery,
  useGetCashBalanceQuery,
  useGetDepositInstructionsQuery,
  useGetHoldingsQuery,
  useGetActivityQuery,
} = accountsApi;
