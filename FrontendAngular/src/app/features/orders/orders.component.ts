import {
  Component,
  OnInit,
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

import { OrderResponse } from '../../core/models/order.model';
import { AuthService } from '../../core/services/auth.service';
import { OrderApiService } from '../../core/services/order-api.service';

@Component({
  selector: 'app-orders',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './orders.component.html',
  styleUrl: './orders.component.css'
})
export class OrdersComponent implements OnInit {
  private readonly orderApiService = inject(OrderApiService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly orders = signal<OrderResponse[]>([]);

  protected readonly isLoading = signal(false);
  protected readonly errorMessage = signal<string | null>(null);

  protected readonly expandedOrderIds = signal<Set<number>>(
    new Set()
  );

  ngOnInit(): void {
    // Проверяем авторизацию перед загрузкой истории.
    if (!this.authService.isAuthenticated) {
      void this.router.navigate(['/auth'], {
        queryParams: {
          returnUrl: '/orders'
        }
      });

      return;
    }

    this.loadOrders();
  }

  // Загрузка истории заказов.
  protected loadOrders(): void {
    this.isLoading.set(true);
    this.errorMessage.set(null);

    this.orderApiService.getMyOrders().subscribe({
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
              returnUrl: '/orders'
            }
          });

          return;
        }

        console.error('Ошибка загрузки истории заказов:', error);

        this.errorMessage.set(
          this.getErrorMessage(error)
        );
      }
    });
  }

  // Повторная загрузка после ошибки.
  protected retry(): void {
    this.loadOrders();
  }

  // Открытие или закрытие состава заказа.
  protected toggleOrder(orderId: number): void {
    this.expandedOrderIds.update(current => {
      const updated = new Set(current);

      if (updated.has(orderId)) {
        updated.delete(orderId);
      } else {
        updated.add(orderId);
      }

      return updated;
    });
  }

  protected isExpanded(orderId: number): boolean {
    return this.expandedOrderIds().has(orderId);
  }

  // Преобразование статуса Backend в понятный текст.
  protected getStatusLabel(status: string): string {
    switch (status.toLowerCase()) {
      case 'pending':
        return 'Ожидает обработки';

      case 'processing':
        return 'В обработке';

      case 'shipped':
        return 'Отправлен';

      case 'completed':
        return 'Выполнен';

      case 'cancelled':
        return 'Отменён';

      default:
        return status;
    }
  }

  // CSS-класс статуса.
  protected getStatusClass(status: string): string {
    switch (status.toLowerCase()) {
      case 'pending':
        return 'pending';

      case 'processing':
        return 'processing';

      case 'shipped':
        return 'shipped';

      case 'completed':
        return 'completed';

      case 'cancelled':
        return 'cancelled';

      default:
        return 'default';
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

  // Форматирование цены.
  protected formatPrice(price: number): string {
    return new Intl.NumberFormat('ru-RU', {
      style: 'currency',
      currency: 'BYN'
    }).format(price);
  }

  private getErrorMessage(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'Произошла непредвиденная ошибка.';
    }

    if (error.status === 0) {
      return 'Не удалось связаться с сервером. Проверьте подключение.';
    }

    if (error.status === 403) {
      return 'У вас нет доступа к истории заказов.';
    }

    if (error.status >= 500) {
      return 'На сервере произошла ошибка. Попробуйте позже.';
    }

    return 'Не удалось загрузить историю заказов.';
  }
}