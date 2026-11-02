import type { LucideIcon } from "lucide-react";
import {
  FileChartColumnIcon,
  LandmarkIcon,
  LayoutDashboardIcon,
  LogOutIcon,
  UserRoundIcon,
  WalletIcon,
} from "lucide-react";
import { NavLink, Outlet, useLocation } from "react-router";
import { useAppSelector } from "@/app/hooks";
import { ModeToggle } from "@/components/mode-toggle";
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

const links: { to: string; label: string; icon: LucideIcon }[] = [
  { to: "/dashboard", label: "Dashboard", icon: LayoutDashboardIcon },
  { to: "/accounts", label: "Accounts", icon: WalletIcon },
  { to: "/reports", label: "Reports", icon: FileChartColumnIcon },
  { to: "/profile", label: "Profile", icon: UserRoundIcon },
];

export function ShellLayout() {
  const me = useAppSelector((state) => state.session.currentUser);
  const { pathname } = useLocation();

  return (
    <SidebarProvider>
      <Sidebar collapsible="icon" variant="inset">
        <SidebarHeader>
          <div className="flex items-center gap-2 px-1 py-1">
            <div className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-sidebar-primary text-sidebar-primary-foreground">
              <LandmarkIcon className="size-4" />
            </div>
            <span className="truncate text-sm font-medium group-data-[collapsible=icon]:hidden">
              Customer Portal
            </span>
          </div>
        </SidebarHeader>
        <SidebarContent>
          <SidebarGroup>
            <SidebarGroupLabel>Navigate</SidebarGroupLabel>
            <SidebarGroupContent>
              <SidebarMenu>
                {links.map((item) => {
                  const Icon = item.icon;
                  return (
                    <SidebarMenuItem key={item.to}>
                      <SidebarMenuButton
                        render={<NavLink to={item.to} />}
                        isActive={pathname === item.to || pathname.startsWith(`${item.to}/`)}
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
        <div className="flex flex-1 flex-col p-4">
          <Outlet />
        </div>
      </SidebarInset>
    </SidebarProvider>
  );
}
