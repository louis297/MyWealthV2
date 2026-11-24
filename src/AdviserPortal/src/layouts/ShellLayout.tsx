import type { ReactNode } from "react";
import type { LucideIcon } from "lucide-react";
import {
  ArrowLeftRightIcon,
  BriefcaseIcon,
  FileChartColumnIcon,
  LandmarkIcon,
  LayoutDashboardIcon,
  LogOutIcon,
  ReceiptIcon,
  UserRoundCogIcon,
  UserRoundIcon,
  UsersIcon,
  WalletIcon,
  ShieldCheckIcon,
  BanknoteArrowUpIcon,
  ClipboardListIcon,
  HandCoinsIcon,
  ImportIcon,
  RefreshCwIcon,
  ScaleIcon,
  PercentIcon,
  SplitIcon,
  PackageIcon,
  PackageSearchIcon,
} from "lucide-react";
import { NavLink, Outlet, useLocation } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import {
  Sidebar,
  SidebarContent,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarInset,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarProvider,
  SidebarRail,
  SidebarTrigger,
} from "@/components/ui/sidebar";
import { startEndSession } from "@/features/session/session";
import {
  canAccessAccounts,
  canAccessAdvisers,
  canAccessTransactions,
  canAccessCustomers,
  canAccessDashboard,
  canAccessFees,
  canAccessInterestRates,
  canAccessCorporateActions,
  canAccessPortfolios,
  canAccessProfile,
  canAccessReports,
  canAccessOrders,
  canAccessRebalances,
  canManageTradeSettlements,
  canManageMigrationBatches,
  canAccessWithdrawals,
  canCheckBankAccounts,
  canManageBankReconciliations,
  canManageStatementLines,
  canAccessInKindTransfers,
  canApproveInKindTransfers,
  canManageCustodyMovements,
  canManageCustodianReconciliations,
} from "@/features/session/roles";
import { useGetCustodyMovementsQuery } from "@/features/custodyMovements/custodyMovementsApi";
import { useGetInKindTransfersQuery } from "@/features/inKindTransfers/inKindTransfersApi";
import { ModeToggle } from '@/components/mode-toggle';

type ShellNavItem = {
  to: string;
  label: string;
  icon: LucideIcon;
  visible: boolean;
  /** in-kind-transfers D88, D90: a count shown beside the label when above 0. */
  count?: number;
};

export function ShellLayout({ children }: { children?: ReactNode }) {
  const me = useAppSelector((state) => state.session.currentUser);
  const tenant = useAppSelector((state) => state.session.tenant);
  const firmName = tenant?.name;
  const { pathname } = useLocation();
  const role = me?.role;
  // D88: a TenantAdmin sees the transfers waiting for approval; D90: the custody movements in suspense.
  const waiting = useGetInKindTransfersQuery({ status: "Requested", pageSize: 1 }, { skip: !canApproveInKindTransfers(role) });
  const suspense = useGetCustodyMovementsQuery({ status: "Unmatched", pageSize: 1 }, { skip: !canManageCustodyMovements(role) });

  const nav: ShellNavItem[] = [
    {
      to: "/dashboard",
      label: "Dashboard",
      icon: LayoutDashboardIcon,
      visible: canAccessDashboard(role),
    },
    {
      to: "/reports",
      label: "Reports",
      icon: FileChartColumnIcon,
      visible: canAccessReports(role),
    },
    {
      to: "/portfolios",
      label: "Portfolios",
      icon: BriefcaseIcon,
      visible: canAccessPortfolios(role),
    },
    {
      to: "/fees",
      label: "Fees",
      icon: ReceiptIcon,
      visible: canAccessFees(role),
    },
    {
      // interest-run D59: under Fees.
      to: "/interest/rates",
      label: "Interest rates",
      icon: PercentIcon,
      visible: canAccessInterestRates(role),
    },
    {
      // corporate-actions D64.
      to: "/corporate-actions",
      label: "Corporate actions",
      icon: SplitIcon,
      visible: canAccessCorporateActions(role),
    },
    {
      to: "/accounts",
      label: "Accounts",
      icon: WalletIcon,
      visible: canAccessAccounts(role),
    },
    {
      to: "/transactions",
      label: "Transactions",
      icon: ArrowLeftRightIcon,
      visible: canAccessTransactions(role),
    },
    {
      to: "/statement-lines",
      label: "Bank receipts",
      icon: LandmarkIcon,
      visible: canManageStatementLines(role),
    },
    {
      to: "/bank-reconciliations",
      label: "Bank reconciliation",
      icon: ScaleIcon,
      visible: canManageBankReconciliations(role),
    },
    {
      to: "/orders",
      label: "Orders",
      icon: ClipboardListIcon,
      visible: canAccessOrders(role),
    },
    {
      // rebalances P19.
      to: "/rebalances",
      label: "Rebalances",
      icon: RefreshCwIcon,
      visible: canAccessRebalances(role),
    },
    {
      // trade-settlement D76.
      to: "/trade-settlements",
      label: "Trade settlements",
      icon: HandCoinsIcon,
      visible: canManageTradeSettlements(role),
    },
    {
      // migration-batches D80.
      to: "/migration-batches",
      label: "Migration batches",
      icon: ImportIcon,
      visible: canManageMigrationBatches(role),
    },
    {
      // in-kind-transfers D88.
      to: "/transfers",
      label: "Transfers",
      icon: PackageIcon,
      visible: canAccessInKindTransfers(role),
      count: waiting.data?.totalCount,
    },
    {
      // in-kind-transfers D90.
      to: "/custody-movements",
      label: "Custody movements",
      icon: PackageSearchIcon,
      visible: canManageCustodyMovements(role),
      count: suspense.data?.totalCount,
    },
    {
      // in-kind-transfers D91.
      to: "/custodian-reconciliations",
      label: "Custodian reconciliation",
      icon: ScaleIcon,
      visible: canManageCustodianReconciliations(role),
    },
    {
      to: "/withdrawals",
      label: "Withdrawals",
      icon: BanknoteArrowUpIcon,
      visible: canAccessWithdrawals(role),
    },
    {
      to: "/nominated-bank-accounts",
      label: "Bank account checks",
      icon: ShieldCheckIcon,
      visible: canCheckBankAccounts(role),
    },
    {
      to: "/customers",
      label: "Customers",
      icon: UsersIcon,
      visible: canAccessCustomers(role),
    },
    {
      to: "/advisers",
      label: "Advisers",
      icon: UserRoundCogIcon,
      visible: canAccessAdvisers(role),
    },
    {
      to: "/profile",
      label: "Profile",
      icon: UserRoundIcon,
      visible: canAccessProfile(role),
    },
  ];

  return (
    <SidebarProvider>
      <Sidebar collapsible="icon" variant="inset">
        <SidebarHeader>
          <div className="flex items-center gap-2 px-1 py-1">
            <div className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-sidebar-primary text-sidebar-primary-foreground">
              <LandmarkIcon className="size-4" />
            </div>
            <span className="truncate text-sm font-medium group-data-[collapsible=icon]:hidden">
              Adviser Portal
            </span>
          </div>
        </SidebarHeader>
        <SidebarContent>
          <SidebarGroup>
            <SidebarGroupLabel>Navigate</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                {nav
                  .filter((item) => item.visible)
                  .map((item) => {
                    const Icon = item.icon;
                    return (
                      <SidebarMenuItem key={item.to}>
                        <SidebarMenuButton
                          render={<NavLink to={item.to} />}
                          isActive={isNavActive(pathname, item.to)}
                          tooltip={item.label}
                        >
                          <Icon />
                          <span>{item.label}</span>
                          {item.count ? (
                            <Badge variant="secondary" className="ml-auto group-data-[collapsible=icon]:hidden">
                              {item.count}
                            </Badge>
                          ) : null}
                        </SidebarMenuButton>
                      </SidebarMenuItem>
                    );
                  })}
              </SidebarMenu>
            </SidebarGroupContent>
          </SidebarGroup>
        </SidebarContent>
        <SidebarRail />
      </Sidebar>
      <SidebarInset>
        <header className="flex min-h-14 shrink-0 flex-wrap items-center gap-2 border-b px-4 py-2">
          <SidebarTrigger />
          <Separator orientation="vertical" className="h-4" />
          {firmName ? <span className="text-sm font-medium">{firmName}</span> : null}
          {me?.tenantCode ? (
            <span className="text-sm text-muted-foreground">{me.tenantCode}</span>
          ) : null}
          <div className="ml-auto flex flex-wrap items-center gap-2">
            {me?.name ? <span className="text-sm">{me.name}</span> : null}
            {me?.role ? <Badge variant="secondary">{me.role}</Badge> : null}
            <ModeToggle />
            <Button variant="outline" size="sm" onClick={() => void startEndSession()}>
              <LogOutIcon />
              Sign out
            </Button>
          </div>
        </header>
        <div className="flex flex-1 flex-col p-4">{children ?? <Outlet />}</div>
      </SidebarInset>
    </SidebarProvider>
  );
}

function isNavActive(pathname: string, to: string) {
  return pathname === to || pathname.startsWith(`${to}/`);
}
