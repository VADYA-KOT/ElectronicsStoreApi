import {
  Component,
  OnInit,
  computed,
  inject,
  signal
} from '@angular/core';

import {
  HttpErrorResponse
} from '@angular/common/http';

import {
  Router,
  RouterLink
} from '@angular/router';

import { OrderResponse } from '../../../core/models/order.model';

import { OrderApiService } from '../../../core/services/order-api.service';

import { AuthService } from '../../../core/services/auth.service';

type OrderStatus =
  | 'Pending'
  | 'Processing'
  | 'Shipped'
  | 'Completed'
  | 'Cancelled';

@Component({
  selector: 'app-admin-orders',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './admin-orders.component.html',
  styleUrl: './admin-orders.component.css'
})
export class AdminOrdersComponent implements OnInit {
  private readonly orderApiService = inject(OrderApiService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly orders = signal<OrderResponse[]>([]);

  protected readonly isLoading = signal(false);

  protected readonly errorMessage = signal<string | null>(null);
  protected readonly successMessage = signal<string | null>(null);

  protected readonly statusFilter = signal('All');

  protected readonly selectedStatuses = signal<Record<number, string>>({});

  protected readonly updatingOrderIds = signal<Set<number>>(new Set());

  protected readonly filteredOrders = computed(() => {
    const filter = this.statusFilter();

    if (filter === 'All') {
      return this.orders();
    }

    return this.orders().filter(
      order => order.status === filter
    );
  });

  ngOnInit(): void {
    if (!this.authService.isAuthenticated ||
        !this.authService.isAdmin) {
      void this.router.navigate(['/']);
      return;
    }

    this.loadOrders();
  }

  // Получение всех заказов.
  protected loadOrders(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.orderApiService.getAllForAdmin().subscribe({
      next: orders => {
        this.orders.set(orders);
        this.isLoading.set(false);
      },

      error: (error: unknown) => {
        this.isLoading.set(false);

        if (
          error instanceof HttpErrorResponse &&
          error.status === 401
        ) {
          void this.router.navigate(['/auth'], {
            queryParams: {
              returnUrl: '/admin/orders'
            }
          });

          return;
        }

        this.errorMessage.set(
          this.getErrorMessage(error)
        );
      }
    });
  }

  // Фильтрация заказов по статусу.
  protected onStatusFilterChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;

    this.statusFilter.set(value);
  }

  // Выбор нового статуса заказа.
  protected onStatusSelectionChange(
    orderId: number,
    event: Event
  ): void {
    const value = (event.target as HTMLSelectElement).value;

    this.selectedStatuses.update(current => ({
      ...current,
      [orderId]: value
    }));
  }

  // Получение доступных следующих статусов.
  protected getAvailableStatuses(status: string): OrderStatus[] {
    switch (status) {
      case 'Pending':
        return ['Processing', 'Cancelled'];

      case 'Processing':
        return ['Shipped', 'Cancelled'];

      case 'Shipped':
        return ['Completed'];

      default:
        return [];
    }
  }

  // Изменение статуса.
  protected updateStatus(order: OrderResponse): void {
    const newStatus = this.selectedStatuses()[order.id];

    if (
      !newStatus ||
      !this.getAvailableStatuses(order.status).includes(
        newStatus as OrderStatus
      ) ||
      this.isUpdating(order.id)
    ) {
      return;
    }

    this.errorMessage.set(null);
    this.successMessage.set(null);

    this.setUpdating(order.id, true);

    this.orderApiService.updateOrderStatus(
      order.id,
      { status: newStatus }
    ).subscribe({
      next: updatedOrder => {
        this.orders.update(current =>
          current.map(item =>
            item.id === updatedOrder.id
              ? updatedOrder
              : item
          )
        );

        this.selectedStatuses.update(current => {
          const updated = { ...current };
          delete updated[order.id];
          return updated;
        });

        this.successMessage.set(
          `Заказ №${updatedOrder.id}: статус изменён на «${this.getStatusLabel(updatedOrder.status)}».`
        );

        this.setUpdating(order.id, false);
      },

      error: (error: unknown) => {
        this.setUpdating(order.id, false);

        if (
          error instanceof HttpErrorResponse &&
          error.status === 401
        ) {
          void this.router.navigate(['/auth'], {
            queryParams: {
              returnUrl: '/admin/orders'
            }
          });

          return;
        }

        this.errorMessage.set(
          this.getErrorMessage(error)
        );
      }
    });
  }

  // Состояние выполнения запроса.
  protected isUpdating(orderId: number): boolean {
    return this.updatingOrderIds().has(orderId);
  }

  private setUpdating(
    orderId: number,
    updating: boolean
  ): void {
    this.updatingOrderIds.update(current => {
      const updated = new Set(current);

      if (updating) {
        updated.add(orderId);
      } else {
        updated.delete(orderId);
      }

      return updated;
    });
  }

  // Перевод статуса на русский язык.
  protected getStatusLabel(status: string): string {
    switch (status) {
      case 'Pending':
        return 'Ожидает обработки';

      case 'Processing':
        return 'В обработке';

      case 'Shipped':
        return 'Отправлен';

      case 'Completed':
        return 'Выполнен';

      case 'Cancelled':
        return 'Отменён';

      default:
        return status;
    }
  }

  // CSS-класс статуса.
  protected getStatusClass(status: string): string {
    switch (status) {
      case 'Pending':
        return 'pending';

      case 'Processing':
        return 'processing';

      case 'Shipped':
        return 'shipped';

      case 'Completed':
        return 'completed';

      case 'Cancelled':
        return 'cancelled';

      default:
        return 'default-status';
    }
  }

  // Форматирование даты.
  protected formatDate(dateUtc: string): string {
    const date = new Date(dateUtc);

    if (Number.isNaN(date.getTime())) {
      return dateUtc;
    }

    return new Intl.DateTimeFormat('ru-RU', {
      day: '2-digit',
      month: 'long',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    }).format(date);
  }

  // Форматирование стоимости.
  protected formatPrice(price: number): string {
    return new Intl.NumberFormat('ru-RU', {
      style: 'currency',
      currency: 'BYN'
    }).format(price);
  }

  // Получение сообщения об ошибке.
  private getErrorMessage(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'Произошла непредвиденная ошибка.';
    }

    if (error.status === 0) {
      return 'Не удалось связаться с сервером.';
    }

    if (error.status === 401) {
      return 'Срок действия авторизации истёк.';
    }

    if (error.status === 403) {
      return 'Недостаточно прав для выполнения операции.';
    }

    if (error.status === 404) {
      return 'Заказ не найден. Обновите список.';
    }

    if (error.status === 409) {
      return this.getServerMessage(error.error) ??
        'Изменение невозможно. Возможно, заказ уже изменён.';
    }

    if (error.status === 400) {
      return this.getServerMessage(error.error) ??
        'Сервер отклонил запрос.';
    }

    if (error.status >= 500) {
      return 'На сервере произошла ошибка.';
    }

    return 'Не удалось выполнить операцию.';
  }

  private getServerMessage(body: unknown): string | null {
    if (typeof body !== 'object' || body === null) {
      return null;
    }

    const response = body as Record<string, unknown>;

    const message = response['message'] ??
      response['detail'] ??
      response['title'];

    return typeof message === 'string' ? message : null;
  }
}