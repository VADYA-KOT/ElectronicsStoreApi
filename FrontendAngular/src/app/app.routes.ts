
import { Routes } from '@angular/router';

import { AuthComponent } from './features/auth/auth.component';
import { CartComponent } from './features/cart/cart.component';
import { ProductDetailsComponent } from './features/products/product-details/product-details.component';
import { OrdersComponent } from './features/orders/orders.component';
import { AccountComponent } from './features/account/account.component';

import { AdminOrdersComponent } from './features/admin/orders/admin-orders.component';
import { AdminProductsComponent } from './features/admin/products/admin-products.component';
import { AdminCategoriesComponent } from './features/admin/categories/admin-categories.component';

import { adminGuard } from './core/guards/admin.guard';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  {
    path: 'auth',
    component: AuthComponent
  },
  {
    path: 'cart',
    component: CartComponent
  },
  {
    path: 'orders',
    component: OrdersComponent
  },
  {
    path: 'account',
    component: AccountComponent,
    canActivate: [authGuard]
  },
  {
    path: 'admin/orders',
    component: AdminOrdersComponent,
    canActivate: [adminGuard]
  },
  {
    path: 'admin/products',
    component: AdminProductsComponent,
    canActivate: [adminGuard]
  },
  {
    path: 'admin/categories',
    component: AdminCategoriesComponent,
    canActivate: [adminGuard]
  },
  {
    path: 'products/:id',
    component: ProductDetailsComponent
  }
];
