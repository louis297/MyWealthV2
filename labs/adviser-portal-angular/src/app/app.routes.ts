import { Routes } from '@angular/router';
import { AdvisersListPage } from '../features/advisers/advisers-list-page';
import { CustomerDetailPage } from '../features/customers/customer-detail-page';
import { CustomersListPage } from '../features/customers/customers-list-page';
import { ProfilePage } from '../features/profile/profile-page';
import { ForbiddenPage } from '../features/session/forbidden-page';
import { allowRoles, homeRedirect, requireSession } from '../features/session/session.guards';
import { HomePage } from '../features/session/home-page';
import { SessionPage } from '../features/session/session-page';
import { roles } from '../features/session/roles';
import { ShellLayout } from '../layouts/shell-layout';

export const routes: Routes = [
  { path: 'session', component: SessionPage },
  { path: 'callback', redirectTo: 'session' },
  { path: 'login', redirectTo: 'session' },
  {
    path: '',
    component: ShellLayout,
    canActivate: [requireSession],
    children: [
      { path: '', pathMatch: 'full', canActivate: [homeRedirect], component: HomePage },
      { path: 'forbidden', component: ForbiddenPage },
      { path: 'profile', canActivate: [allowRoles([roles.tenantAdmin, roles.adviser, roles.systemAdmin])], component: ProfilePage },
      {
        path: 'customers',
        canActivate: [allowRoles([roles.tenantAdmin, roles.adviser])],
        children: [
          { path: '', component: CustomersListPage },
          { path: ':id', component: CustomerDetailPage },
        ],
      },
      {
        path: 'advisers',
        canActivate: [allowRoles([roles.tenantAdmin])],
        children: [
          { path: '', component: AdvisersListPage },
          { path: 'new', component: AdvisersListPage },
        ],
      },
    ],
  },
];
