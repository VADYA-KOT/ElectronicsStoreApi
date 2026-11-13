
import {
  Injectable,
  computed,
  effect,
  inject,
  signal
} from '@angular/core';

import { Product } from '../models/product.model';
import { CartItem } from '../models/cart-item.model';

import { AuthService } from './auth.service';

const CART_STORAGE_KEY = 'electronics-store-cart';
const GUEST_CART_STORAGE_KEY = `${CART_STORAGE_KEY}:guest`;
const USER_CART_STORAGE_PREFIX = `${CART_STORAGE_KEY}:user:`;

@Injectable({
  providedIn: 'root'
})
export class CartService {
  private readonly authService = inject(AuthService);

  // Текущий ключ хранения корзины.
  private activeStorageKey = this.getStorageKey(
    this.authService.currentUserId()
  );

  private readonly itemsState = signal<CartItem[]>(
    this.readFromStorage(this.activeStorageKey)
  );

  constructor() {
    // Автоматическое переключение корзины при смене аккаунта.
    effect(() => {
      const userId = this.authService.currentUserId();

      const newStorageKey = this.getStorageKey(userId);

      if (newStorageKey === this.activeStorageKey) {
        return;
      }

      this.activeStorageKey = newStorageKey;

      this.itemsState.set(
        this.readFromStorage(newStorageKey)
      );
    });
  }

  readonly items = this.itemsState.asReadonly();

  // Общее количество единиц товаров.
  readonly totalQuantity = computed(() =>
    this.itemsState().reduce(
      (total, item) => total + item.quantity,
      0
    )
  );

  // Общая стоимость корзины.
  readonly totalPrice = computed(() =>
    this.itemsState().reduce(
      (total, item) => total + item.product.price * item.quantity,
      0
    )
  );

  // Добавление товара или увеличение его количества.
  addItem(product: Product): boolean {
    if (product.quantity <= 0) {
      return false;
    }

    let added = false;

    this.itemsState.update(items => {
      const existingItem = items.find(
        item => item.product.id === product.id
      );

      if (existingItem) {
        // Нельзя превысить доступный остаток.
        if (existingItem.quantity >= product.quantity) {
          return items;
        }

        added = true;

        return items.map(item =>
          item.product.id === product.id
            ? {
                product,
                quantity: item.quantity + 1
              }
            : item
        );
      }

      added = true;

      return [
        ...items,
        {
          product,
          quantity: 1
        }
      ];
    });

    if (added) {
      this.saveToStorage();
    }

    return added;
  }

  // Изменение количества товара.
  updateQuantity(productId: number, quantity: number): void {
    if (!Number.isInteger(quantity)) {
      return;
    }

    if (quantity <= 0) {
      this.removeItem(productId);
      return;
    }

    this.itemsState.update(items =>
      items.map(item => {
        if (item.product.id !== productId) {
          return item;
        }

        const maximumQuantity = item.product.quantity > 0
          ? item.product.quantity
          : item.quantity;

        return {
          ...item,
          quantity: Math.min(quantity, maximumQuantity)
        };
      })
    );

    this.saveToStorage();
  }

  // Удаление отдельного товара.
  removeItem(productId: number): void {
    this.itemsState.update(items =>
      items.filter(item => item.product.id !== productId)
    );

    this.saveToStorage();
  }

  // Полная очистка корзины.
  clearCart(): void {
    this.itemsState.set([]);
    this.saveToStorage();
  }

  // Получение ключа корзины.
  private getStorageKey(userId: number | null): string {
    if (userId === null) {
      return GUEST_CART_STORAGE_KEY;
    }

    return `${USER_CART_STORAGE_PREFIX}${userId}`;
  }

  // Сохранение корзины в localStorage.
  private saveToStorage(): void {
    try {
      localStorage.setItem(
        this.activeStorageKey,
        JSON.stringify(this.itemsState())
      );
    } catch (error: unknown) {
      console.error('Не удалось сохранить корзину:', error);
    }
  }

  // Загрузка сохранённой корзины.
  private readFromStorage(storageKey: string): CartItem[] {
    try {
      if (typeof localStorage === 'undefined') {
        return [];
      }

      // Однократный перенос старой общей корзины
      // в корзину текущего пользователя или гостя.
      const legacyData = localStorage.getItem(CART_STORAGE_KEY);

      if (legacyData !== null) {
        const currentData = localStorage.getItem(storageKey);

        if (currentData === null) {
          localStorage.setItem(storageKey, legacyData);
        }

        localStorage.removeItem(CART_STORAGE_KEY);
      }

      const savedData = localStorage.getItem(storageKey);

      if (!savedData) {
        return [];
      }

      const parsed: unknown = JSON.parse(savedData);

      if (!Array.isArray(parsed)) {
        return [];
      }

      return parsed.filter(
        (item): item is CartItem => this.isValidCartItem(item)
      );
    } catch (error: unknown) {
      console.error('Не удалось загрузить корзину:', error);
      return [];
    }
  }

  // Проверка данных, полученных из localStorage.
  private isValidCartItem(value: unknown): value is CartItem {
    if (typeof value !== 'object' || value === null) {
      return false;
    }

    const item = value as Record<string, unknown>;
    const product = item['product'];

    if (typeof product !== 'object' || product === null) {
      return false;
    }

    const productData = product as Record<string, unknown>;

    return (
      Number.isInteger(item['quantity']) &&
      Number(item['quantity']) > 0 &&
      typeof productData['id'] === 'number' &&
      typeof productData['name'] === 'string' &&
      typeof productData['price'] === 'number' &&
      typeof productData['quantity'] === 'number' &&
      Array.isArray(productData['specifications'])
    );
  }
}
