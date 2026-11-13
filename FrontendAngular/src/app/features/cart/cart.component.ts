import {
  Component,
  computed,
  inject,
  signal
} from '@angular/core';

import {
  Router,
  RouterLink
} from '@angular/router';

import { HttpErrorResponse } from '@angular/common/http';

import { CartItem } from '../../core/models/cart-item.model';

import {
  CartService
} from '../../core/services/cart.service';

import {
  ProductService
} from '../../core/services/product.service';

import {
  AuthService
} from '../../core/services/auth.service';

import {
  OrderApiService
} from '../../core/services/order-api.service';

import {
  InsufficientStockItemResponse
} from '../../core/models/order.model';

@Component({
  selector: 'app-cart',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './cart.component.html',
  styleUrl: './cart.component.css'
})
export class CartComponent {
  private readonly cartService = inject(CartService);
  private readonly productService = inject(ProductService);
  private readonly authService = inject(AuthService);
  private readonly orderApiService = inject(OrderApiService);
  private readonly router = inject(Router);

  protected readonly items = this.cartService.items;
  protected readonly totalQuantity = this.cartService.totalQuantity;
  protected readonly totalPrice = this.cartService.totalPrice;

  protected readonly notice = signal<string | null>(null);
  protected readonly isSubmitting = signal(false);

  protected readonly isAuthenticated = computed(
    () => this.authService.isAuthenticated
  );

  // Увеличение количества.
  protected increase(item: CartItem): void {
    const added = this.cartService.addItem(item.product);

    this.notice.set(
      added
        ? null
        : `Достигнуто максимальное доступное количество товара «${item.product.name}».`
    );
  }

  // Уменьшение количества.
  protected decrease(item: CartItem): void {
    this.notice.set(null);

    this.cartService.updateQuantity(
      item.product.id,
      item.quantity - 1
    );
  }

  // Удаление отдельного товара.
  protected remove(productId: number): void {
    this.cartService.removeItem(productId);
    this.notice.set(null);
  }

  // Очистка всей корзины.
  protected clearCart(): void {
    this.cartService.clearCart();
    this.notice.set(null);
  }

  // Оформление заказа.
  protected checkout(): void {
    if (this.isSubmitting()) {
      return;
    }

    if (this.items().length === 0) {
      this.notice.set('Корзина пуста.');
      return;
    }

    if (!this.authService.isAuthenticated) {
      this.notice.set('Для оформления заказа необходимо войти в аккаунт.');

      void this.router.navigate(['/auth'], {
        queryParams: {
          returnUrl: '/cart'
        }
      });

      return;
    }

    this.notice.set(null);
    this.isSubmitting.set(true);

    const request = {
      items: this.items().map(item => ({
        productId: item.product.id,
        quantity: item.quantity
      }))
    };

    this.orderApiService.createOrder(request).subscribe({
      next: order => {
        this.cartService.clearCart();

        this.notice.set(
          `Заказ №${order.id} успешно оформлен. Сумма заказа: ${this.formatPrice(order.totalAmount)}.`
        );

        this.isSubmitting.set(false);
      },

      error: (error: unknown) => {
        this.isSubmitting.set(false);

        if (
          error instanceof HttpErrorResponse &&
          error.status === 401
        ) {
          this.notice.set(
            'Срок действия авторизации истёк. Войдите в аккаунт повторно.'
          );

          void this.router.navigate(['/auth'], {
            queryParams: {
              returnUrl: '/cart'
            }
          });

          return;
        }

        this.notice.set(this.getOrderErrorMessage(error));
      }
    });
  }

  // Формирование сообщения об ошибке оформления заказа.
  private getOrderErrorMessage(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) {
      return 'Произошла непредвиденная ошибка при оформлении заказа.';
    }

    if (error.status === 0) {
      return 'Не удалось связаться с сервером. Проверьте подключение.';
    }

    if (error.status === 400) {
      return this.getServerMessage(error.error) ??
        'Сервер отклонил заказ. Проверьте данные корзины.';
    }

    if (error.status === 404) {
      return 'Один из товаров больше не найден. Обновите корзину.';
    }

    if (error.status === 409) {
      const stockMessage = this.getStockErrorMessage(error.error);

      return stockMessage ??
        this.getServerMessage(error.error) ??
        'Не удалось оформить заказ. Возможно, остатки товаров изменились. Проверьте корзину.';
    }

    if (error.status >= 500) {
      return 'На сервере произошла ошибка. Попробуйте оформить заказ позже.';
    }

    return this.getServerMessage(error.error) ??
      'Не удалось оформить заказ.';
  }

  // Получение сообщения от Backend.
  private getServerMessage(body: unknown): string | null {
    if (typeof body !== 'object' || body === null) {
      return null;
    }

    const response = body as Record<string, unknown>;

    const message =
      response['message'] ??
      response['title'] ??
      response['detail'];

    return typeof message === 'string'
      ? message
      : null;
  }

  // Формирование подробного сообщения о нехватке товара.
  private getStockErrorMessage(body: unknown): string | null {
    if (typeof body !== 'object' || body === null) {
      return null;
    }

    const response = body as Record<string, unknown>;

    const rawItems =
      response['insufficientStockItems'] ??
      response['items'] ??
      response['InsufficientStockItems'];

    if (!Array.isArray(rawItems) || rawItems.length === 0) {
      return null;
    }

    const stockItems = rawItems as InsufficientStockItemResponse[];

    const details = stockItems.map(item =>
      `${item.productName}: заказано ${item.requestedQuantity}, доступно ${item.availableQuantity}`
    );

    return `Недостаточно товаров на складе: ${details.join('; ')}. Измените количество в корзине.`;
  }

  // Форматирование стоимости.
  protected formatPrice(price: number): string {
    return new Intl.NumberFormat('ru-RU', {
      style: 'currency',
      currency: 'BYN'
    }).format(price);
  }

  // Формирование адреса изображения.
  protected getImageUrl(imageUrl: string | null): string | null {
    return this.productService.getImageUrl(imageUrl);
  }
}