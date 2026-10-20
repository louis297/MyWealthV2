import type { ReactNode } from "react";
import type { LucideIcon } from "lucide-react";
import {
  ArrowLeftRightIcon,
  FileChartColumnIcon,
  LandmarkIcon,
  LayoutDashboardIcon,
  LogOutIcon,
  UserRoundCogIcon,
  UserRoundIcon,
  UsersIcon,
  WalletIcon,
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
  canAccessProfile,
  canAccessReports,
  roles,
} from "@/features/session/roles";

type ShellNavItem = {
  to: string;
  label: string;
  icon: LucideIcon;
  visible: boolean;
};

export function ShellLayout({ children }: { children?: ReactNode }) {
  const me = useAppSelector((state) => state.session.currentUser);
  const tenant = useAppSelector((state) => state.session.tenant);
  const firmName = me?.role === roles.systemAdmin ? "Platform" : tenant?.name;
  const { pathname } = useLocation();
  const role = me?.role;

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
