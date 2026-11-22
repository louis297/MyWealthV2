import { api } from "@/shared/api/api";
import type { PagedList } from "@/shared/types/page";

/** trade-fees §8.1: the fee detail of a `Fee` cash leg. `amount` and `basisAmount` are in the quote currency. */
export type TransactionFee = {
  kind: string;
  /** The schedule used. Null for a fee kind without a schedule. */
  scheduleId: string | null;
  basisAmount: number;
  amount: number;
  currency: string;
};

export type TransactionCash = {
  accountId: string;
  customerId: string;
  /** `Principal`, `Fee` (trade-fees §8.1) or `Tax` (interest-run R34). */
  role: string;
  /** In the Account currency. */
  amount: number;
  currency: string;
  /** Set on a `Fee` leg; null on a `Principal` leg. */
  fee: TransactionFee | null;
  /** interest-run R34: set on a `Tax` leg; null on any other. */
  tax?: TransactionTax | null;
};

/** interest-run R34: how a `Tax` leg was computed. `amount` is the tax, positive. */
export type TransactionTax = {
  kind: string;
  ratePercent: number;
  basisAmount: number;
  amount: number;
  currency: string;
};

/** interest-run R34: the period an interest posting pays. */
export type TransactionInterest = {
  periodFrom: string;
  periodTo: string;
  days: number;
  accruedAmount: number;
  grossAmount: number;
};

export type TransactionSecurity = {
  accountId: string;
  customerId: string;
  instrumentId: string;
  quantity: number;
  cost: number;
  costCurrency: string;
};

export type TransactionFx = {
  baseCurrency: string;
  quoteCurrency: string;
  /** Quote currency per 1 base currency. */
  rate: number;
  rateAsOfDate: string;
};

/** The process that wrote a transaction (transaction-source R10). `id` is the rebalance handle, or null. */
export type TransactionSource = {
  kind: string;
  id: string | null;
};

export type TransactionBankReceipt = {
  valueDate: string;
  reference: string | null;
  payerName: string | null;
  payerAccount: string | null;
};

export type Transaction = {
  id: string;
  tenantId: string;
  type: string;
  source?: TransactionSource;
  /** Legs on accounts the caller can see; each carries its own account and customer. */
  cash: TransactionCash[];
  bookedAt: string;
  memo?: string | null;
  reference?: string | null;
  originalTransactionId?: string | null;
  reversedByTransactionId?: string | null;
  rowVersion: string;
  security: TransactionSecurity[];
  /** The rate the transaction used (FX conversions and their reversals); otherwise null. */
  fx?: TransactionFx | null;
  /** The reason of an `Adjustment` and of its Reversal; otherwise null (adjustments R21). */
  adjustment?: { reason: string } | null;
  /** deposit-matching R36: the bank's details on a deposit and on its Reversal; otherwise null. */
  bankReceipt?: TransactionBankReceipt | null;
  /** withdrawals R41: the bank's details on a payout and on its Reversal; otherwise null. */
  payment?: TransactionPayment | null;
  /** interest-run R34: on an interest posting and its Reversal; otherwise null. */
  interest?: TransactionInterest | null;
  /** corporate-actions R37: on an entitlement posting and its Reversal; otherwise null. */
  corporateAction?: TransactionCorporateAction | null;
};

/** corporate-actions R37: the event behind an entitlement posting. */
export type TransactionCorporateAction = {
  id: string;
  kind: string;
  ratioNew: number;
  ratioHeld: number;
  exDate: string;
  recordDate: string | null;
  paymentDate: string | null;
  quantityBefore: number;
  quantityAfter: number;
};

export type TransactionPayment = {
  valueDate: string;
  reference: string | null;
  payeeName: string | null;
  payeeAccount: string | null;
};

export type TransactionListArgs = {
  page?: number;
  accountId?: string;
  customerId?: string;
  type?: string;
};

/**
 * `bookedAt` is sent for an External account only; the server sets it on a Custodial one (custody-modes R10).
 * An External `Buy` / `Sell` carries the executed `price`, an optional `fee` and, across currencies, `fxRate`
 * (external-caller-numbers R5–R7); a Custodial one carries none of them.
 */
export type CreateSecurityTransactionBody = {
  accountId: string;
  type: string;
  bookedAt?: string;
  memo: string | null;
  reference: string | null;
  security: { instrumentId: string; quantity: number; cost?: number; price?: number }[];
  fee?: number;
  fxRate?: number;
};

export type CreateCashTransactionBody = {
  accountId: string;
  type: "TransferIn" | "TransferOut" | "CloseOut";
  amount: number;
  bookedAt?: string;
  memo: string | null;
};

export type CreateInternalTransferBody = {
  fromAccountId: string;
  toAccountId: string;
  amount: number;
  bookedAt?: string;
  memo: string | null;
  reference: string | null;
};

/** Exactly one of sellAmount / buyAmount. */
export type FxQuoteBody = {
  fromAccountId: string;
  toAccountId: string;
  sellAmount?: number;
  buyAmount?: number;
  bookedAt?: string;
};

export type FxQuote = {
  baseCurrency: string;
  quoteCurrency: string;
  rate: number;
  rateAsOfDate: string;
  sell: { amount: number; currency: string };
  buy: { amount: number; currency: string };
};

/**
 * Between Custodial Accounts: one amount and the quoted `expectedRate`. Between External Accounts: both amounts and
 * no `expectedRate` (external-caller-numbers R15, R16).
 */
export type CreateFxConversionBody = FxQuoteBody & {
  expectedRate?: number;
  bookedAt?: string;
  memo: string | null;
  reference: string | null;
};

export type CreateTransactionBody = CreateSecurityTransactionBody | CreateCashTransactionBody;

/**
 * Adjustments §8.1: one Custodial account, a reason, a memo, and a signed cash `amount`, a one-instrument holding
 * change, or both. No `bookedAt`: the server books it.
 */
export type CreateAdjustmentBody = {
  accountId: string;
  reason: string;
  memo: string;
  reference: string | null;
  amount?: number;
  security?: { instrumentId: string; quantityChange: number; costChange: number };
};

export const transactionsApi = api.injectEndpoints({
  endpoints: (builder) => ({
    getTransactions: builder.query<PagedList<Transaction>, TransactionListArgs | void>({
      query: (args) => {
        const params = new URLSearchParams();
        params.set("page", String(args?.page ?? 1));
        params.set("pageSize", "20");
        if (args?.accountId) {
          params.set("accountId", args.accountId);
        }
        if (args?.customerId) {
          params.set("customerId", args.customerId);
        }
        if (args?.type) {
          params.set("type", args.type);
        }
        return `/transactions?${params.toString()}`;
      },
      providesTags: ["Transaction"],
    }),
    getTransaction: builder.query<Transaction, string>({
      query: (id) => `/transactions/${id}`,
      providesTags: (_result, _error, id) => [{ type: "Transaction", id }],
    }),
    createTransaction: builder.mutation<
      { id: string },
      { body: CreateTransactionBody; idempotencyKey: string }
    >({
      query: ({ body, idempotencyKey }) => ({
        url: "/transactions",
        method: "POST",
        body,
        headers: { "Idempotency-Key": idempotencyKey },
      }),
      invalidatesTags: ["Transaction", "Account"],
    }),
    createInternalTransfer: builder.mutation<
      { id: string },
      { body: CreateInternalTransferBody; idempotencyKey: string }
    >({
      query: ({ body, idempotencyKey }) => ({
        url: "/transactions/transfer",
        method: "POST",
        body,
        headers: { "Idempotency-Key": idempotencyKey },
      }),
      invalidatesTags: ["Transaction", "Account"],
    }),
    getFxQuote: builder.query<FxQuote, FxQuoteBody>({
      query: (body) => ({ url: "/transactions/fx/quote", method: "POST", body }),
      keepUnusedDataFor: 0,
    }),
    createFxConversion: builder.mutation<
      { id: string },
      { body: CreateFxConversionBody; idempotencyKey: string }
    >({
      query: ({ body, idempotencyKey }) => ({
        url: "/transactions/fx",
        method: "POST",
        body,
        headers: { "Idempotency-Key": idempotencyKey },
      }),
      invalidatesTags: ["Transaction", "Account"],
    }),
    createAdjustment: builder.mutation<{ id: string }, { body: CreateAdjustmentBody; idempotencyKey: string }>({
      query: ({ body, idempotencyKey }) => ({
        url: "/transactions/adjustment",
        method: "POST",
        body,
        headers: { "Idempotency-Key": idempotencyKey },
      }),
      invalidatesTags: ["Transaction", "Account"],
    }),
    reverseTransaction: builder.mutation<{ id: string }, { id: string; idempotencyKey: string }>({
      query: ({ id, idempotencyKey }) => ({
        url: `/transactions/${id}/reverse`,
        method: "POST",
        body: {},
        headers: { "Idempotency-Key": idempotencyKey },
      }),
      invalidatesTags: ["Transaction", "Account"],
    }),
  }),
});

export const {
  useGetTransactionsQuery,
  useGetTransactionQuery,
  useCreateTransactionMutation,
  useCreateInternalTransferMutation,
  useGetFxQuoteQuery,
  useCreateFxConversionMutation,
  useCreateAdjustmentMutation,
  useReverseTransactionMutation,
} = transactionsApi;
